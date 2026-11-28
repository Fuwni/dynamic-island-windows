using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
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
    /// <summary>
    /// The press (Morph island: 520 / 18). In hundredths — 100 is full size — because Spring settles within
    /// 0.15 of its target, which on a plain 0…1 scale would end the dip before it is seen.
    /// </summary>
    private readonly Spring _press = new(100) { Stiffness = 520, Damping = 18 };
    private bool _hShrinking;
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
        HomeMissed.Resized += RefitIsland;
        _shrinkTimer.Tick += (_, _) => ShrinkNow();
        // Esc folds the open island (when nothing inside used the key first, e.g. the app search)
        KeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Escape || e.Handled || _mode != Mode.Expanded) return;
            _pinnedOpen = false;
            SetMode(Mode.Compact);
            e.Handled = true;
        };
        ShadowHost.RenderTransform = Island.RenderTransform;   // the shadow moves, squashes and bounces with the island
        Services.Loc.TranslateTree(this);
        if (ContextMenu != null) Services.Loc.TranslateTree(ContextMenu);
        TitleCaseCaptions(this);
        // pill buttons on the island are interactive glass: pressed, they swell a little
        foreach (var ev in new[] { PreviewMouseLeftButtonDownEvent, PreviewMouseLeftButtonUpEvent, MouseLeaveEvent })
        {
            bool down = ev == PreviewMouseLeftButtonDownEvent;
            EventManager.RegisterClassHandler(typeof(System.Windows.Controls.Primitives.ButtonBase), ev, new RoutedEventHandler((s, _) => SwellPill((FrameworkElement)s, down)));
        }
        // every context menu of the island (right click, voices, apps, themes): above the island while it is open
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler((m, _) =>
        {
            _menusOpen++;
            if (PresentationSource.FromVisual((Visual)m) is System.Windows.Interop.HwndSource src) Native.KeepTopmost(src.Handle);
            // it grows out of a pill (Dynamic menu), its glass with it
            ((ContextMenu)m).Dispatcher.BeginInvoke(() => Views.MenuMorph.Open((ContextMenu)m, MenuGlass), System.Windows.Threading.DispatcherPriority.Loaded);
        }));
        // menus are glass where Windows can blur (and the person hasn't turned transparency off)
        Application.Current.Resources["MenuBg"] = new SolidColorBrush(MenuGlass ? Color.FromArgb(0xA8, 0x1E, 0x1E, 0x24) : Color.FromArgb(0xF2, 0x1E, 0x1E, 0x22));
        EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.ClosedEvent, new RoutedEventHandler((_, _) => _menusOpen = Math.Max(0, _menusOpen - 1)));

        PlaceWindow();
        _tick.Tick += (_, _) => Measure("tick", OnTick);
        _gaze.Tick += (_, _) => Measure("gaze", UpdateGaze);
        _blink.Tick += (_, _) => Blink();
        // hover: not while typing (the cursor just happens to sit there), and on the tab that fits the moment
        _expandDelay.Tick += (_, _) => { _expandDelay.Stop(); if (_mode == Mode.Compact && !_dragging && !Typing) OpenSmart(); };
        _collapseDelay.Tick += (_, _) => { _collapseDelay.Stop(); TryCollapse(); };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); if (_mode == Mode.Toast && !Island.IsMouseOver) SetMode(Mode.Compact); };
        _watch.Tick += (_, _) => Measure("fullscreen watch", WatchFullscreen);

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

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")] private static extern int DwmFlush();

    private async Task OnLoadedAsync(string[] args)
    {
#if DEBUG   // tests, pictures and demo videos: not in the release build
        int snap = Array.IndexOf(args, "--snapshot");
        int promo = Array.IndexOf(args, "--promo");
        int review = Array.IndexOf(args, "--review");
        int live = Array.IndexOf(args, "--live-demo");
        int tiktok = Array.IndexOf(args, "--tiktok"), tiktokScreen = Array.IndexOf(args, "--tiktok-screen");
        if (snap >= 0 || promo >= 0 || review >= 0 || live >= 0 || tiktok >= 0 || tiktokScreen >= 0 || args.Contains("--cover")) BeginDemo();
        if (live >= 0) StartLiveDemo(args.ElementAtOrDefault(live + 1) ?? "demo-commands.txt");
#endif

        // the last start never got as far as showing the island: light graphics before any glass or effect runs
        bool rescued = StartupGuard.PreviousFailed && _settings.LiteGraphics != true;
        if (rescued)
        {
            _settings.LiteGraphics = true;
            _settings.Save();
            Log.Write("start: the last start did not finish — light graphics switched on");
        }
        // up for a few seconds: this start counts as good
        var up = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        up.Tick += (_, _) => { up.Stop(); StartupGuard.Done(); };
        up.Start();
        Safely("theme", ApplyThemeLocal);
        Safely("character", () => Character.Kind = _settings.Character);
        Safely("glass", InitGlass);
        Safely("shape", () => UpdateShape(false));
        Safely("home", InitHome);
        Safely("settings page", InitSettingsPage);
        Safely("buddy", InitBuddyReactions);
        Safely("updates", InitUpdates);
        Safely("control center", InitControlCenter);
        Safely("apps", InitApps);
        Safely("photo", ApplyPhoto);
        Safely("scene", ApplyScene);
        Safely("look", ApplyLook);
        Safely("island size", ApplyIslandScale);
        Safely("claude", InitClaude);
        Safely("recording", InitRecording);
        Safely("extras", InitExtras);

        OnTick();
        _tick.Start();
        _ = Dispatcher.InvokeAsync(ShrinkWindowToPill, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        _gaze.Start();
        _watch.Start();
        ScheduleBlink();
        Safely("tray", CreateTray);

        if (rescued)
            _ = Dispatcher.InvokeAsync(() => ShowToast("\uE7BA", L.T("Включена облегчённая графика"),
                L.T("Прошлый запуск не удался, поэтому эффекты выключены. Вернуть: Настройки → Поведение."), ms: 8000),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (_settings.Hidden) HideIsland(animate: false);

#if DEBUG   // tests, pictures and demo videos: not in the release build
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
                    "calendar" => TabCalendar, "system" => TabSystem, "widgets" => TabWidgets,
                    "settings" => TabSettings, _ => TabHome,
                };
                tab.IsChecked = true;
            }
        }
#endif

#if DEBUG   // tests, pictures and demo videos: not in the release build
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
                if (action == "themeswitch")   // test (2.1): glass themes switched over a page that stands still: the picture shown where the theme has one, gone where it has none
                {
                    Settings.ReadOnly = true;
                    Island.IsHitTestVisible = false; _settings.ExpandOnHover = false; _settings.LiteGraphics = false; NoClickOutside = true;
                    await Task.Delay(2500);
                    var cover = LookBackdrop("colour");
                    foreach (var id in new[] { "liquid", "clear", "liquid", "matte", "clear", "matte", "graphite", "clear", "graphite" })
                    {
                        ApplyThemePreset(BuiltInThemes.First(t => t.Id == id));
                        foreach (bool open in new[] { true, false })
                        {
                            _pinnedOpen = open;
                            if (open) Expand(); else SetMode(Mode.Compact);
                            await Task.Delay(2200);
                            bool picture = FrostHost.Opacity > 0.5 && _frostCovers;
                            double fill = IslandShape.Fill is LinearGradientBrush g ? g.GradientStops[2].Color.A / 255.0 : 1;
                            bool ok = id == "clear" ? !picture && fill is > 0.1 and < 0.97 : picture;
                            Log.Write($"themeswitch: {id} {(open ? "open" : "folded")}: picture={picture}, fill {fill:0.00}, lens={FrostLens.Effect != null} -> {(ok ? "ok" : "FAIL")}");
                        }
                    }
                    cover.Close();
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "glass")   // test: is the island opaque while the frosted picture doesn't fit it?
                {
                    Settings.ReadOnly = true;
                    _settings.Glass = true; ApplyGlass();
                    string State() => $"frost covers={_frostCovers}, body alpha={((LinearGradientBrush)IslandShape.Fill).GradientStops[1].Color.A}, bottom alpha={((LinearGradientBrush)IslandShape.Fill).GradientStops[3].Color.A}";
                    await Task.Delay(5000);
                    Log.Write("glass: folded, settled: " + State());
                    TabHome.IsChecked = true; _pinnedOpen = true; Expand();
                    await Task.Delay(60);
                    Log.Write("glass: opening: " + State());
                    await Task.Delay(1500);
                    Log.Write("glass: open, settled: " + State());
                    _pinnedOpen = false; SetMode(Mode.Compact);
                    await Task.Delay(60);
                    Log.Write("glass: closing: " + State());
                    await Task.Delay(1500);
                    Log.Write("glass: folded again: " + State());
                    await Task.Delay(5000);
                    Log.Write("glass: folded, 6 s later: " + State());
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "weathertest")   // test: the weather fallbacks for places Open-Meteo can't serve
                {
                    Settings.ReadOnly = true;
                    foreach (var fb in new[] { false, true })
                        foreach (var city in new[] { "Москва", "Новозыбков", "Kyiv" })
                        {
                            var ws = new WeatherService { TestFallback = fb };
                            try
                            {
                                var place = await ws.ResolveAsync(city);
                                if (place == null) { Log.Write($"weathertest: fallback={fb} {city}: not found"); continue; }
                                var w = await ws.GetAsync(place);
                                Log.Write($"weathertest: fallback={fb} {city} -> {place.Name} ({place.Lat:0.00},{place.Lon:0.00}) via {ws.Source}: {w.Temp:0.0}° feels {w.FeelsLike:0.0}°, {WeatherService.Describe(w.Code)}, min {w.Min:0} max {w.Max:0}, wind {w.WindSpeed:0.0}, hum {w.Humidity:0}, press {w.PressureMm:0}, sun {w.Sunrise:HH:mm}-{w.Sunset:HH:mm}, hours {w.Hours.Count} first {w.Hours.FirstOrDefault()?.Time:HH:mm} {w.Hours.FirstOrDefault()?.Temp:0}°");
                            }
                            catch (Exception e) { Log.Write($"weathertest: fallback={fb} {city}: {e.Message}"); }
                            await Task.Delay(1100);   // OpenStreetMap: at most one request a second
                        }
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "keypill")   // pictures of the Caps Lock / layout pill, folded and open (with --cover)
                {
                    var dir = args.ElementAtOrDefault(r + 2) ?? System.IO.Path.GetTempPath();
                    Island.IsHitTestVisible = false; _settings.ExpandOnHover = false;
                    await Task.Delay(6000);
                    void Shot(string name)
                    {
                        var rtb = new RenderTargetBitmap((int)Root.ActualWidth, (int)Root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                        rtb.Render(Root);
                        CaptureService.SavePng(rtb, System.IO.Path.Combine(dir, name + ".png"));
                    }
                    SetMode(Mode.Compact); await Task.Delay(1200);
                    ShowKeyPill("", L.T("Caps Lock включён"), null, Color.FromRgb(0xFF, 0x9F, 0x0A));
                    await Task.Delay(400); Shot("pill-compact");
                    TabMusic.IsChecked = true; _pinnedOpen = true; Expand(); await Task.Delay(1500);
                    ShowKeyPill("", "EN", "English", null);
                    await Task.Delay(400); Shot("pill-open");
                    Log.Write($"keypill: mode after = {_mode}");
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "capturetest")   // test: does the glass's screen grab see the island itself without the capture exclusion?
                {
                    Settings.ReadOnly = true;
                    var dir = args.ElementAtOrDefault(r + 2) ?? System.IO.Path.GetTempPath();
                    _settings.ExpandOnHover = false;
                    TabHome.IsChecked = true; _pinnedOpen = true; Expand();
                    await Task.Delay(2500);
                    var tl = Island.PointToScreen(new Point(0, 0));
                    var br = Island.PointToScreen(new Point(Island.ActualWidth, Island.ActualHeight));
                    var rect = new System.Drawing.Rectangle((int)tl.X, (int)tl.Y, (int)(br.X - tl.X), (int)(br.Y - tl.Y));
                    foreach (var hide in new[] { true, false })
                    {
                        _settings.CaptureHidden = hide; ApplyCaptureExclusion();
                        await Task.Delay(800);
                        int w = rect.Width / 2, h = rect.Height / 2;
                        var px = CaptureService.CaptureSmall(rect, w, h, null)!;
                        double sum = 0; for (int i = 0; i < px.Length; i += 4) sum += px[i] + px[i + 1] + px[i + 2];
                        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, px, w * 4);
                        CaptureService.SavePng(bmp, System.IO.Path.Combine(dir, $"grab-exclude-{hide}.png"));
                        Log.Write($"capturetest: exclude={hide}: mean brightness {sum / (px.Length / 4 * 3):0}");
                    }
                    // the pulse: exclude only for the grab
                    foreach (var flush in new[] { false, true, false, true })
                    {
                        int w = rect.Width / 2, h = rect.Height / 2;
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        Native.SetWindowDisplayAffinity(_hwnd, Native.WDA_EXCLUDEFROMCAPTURE);
                        if (flush) DwmFlush();
                        var px = CaptureService.CaptureSmall(rect, w, h, null)!;
                        Native.SetWindowDisplayAffinity(_hwnd, 0);
                        double sum = 0; for (int i = 0; i < px.Length; i += 4) sum += px[i] + px[i + 1] + px[i + 2];
                        Log.Write($"capturetest: pulse flush={flush}: mean brightness {sum / (px.Length / 4 * 3):0}, {sw.Elapsed.TotalMilliseconds:0.0} ms");
                        await Task.Delay(500);
                    }
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "newui")   // pictures of the 1.8 additions (with --cover): level pill, progress, theme editor, self-check, welcome, what's new
                {
                    var dir = args.ElementAtOrDefault(r + 2) ?? System.IO.Path.GetTempPath();
                    Island.IsHitTestVisible = false; _settings.ExpandOnHover = false;
                    await Task.Delay(5000);
                    void Shot(string name, FrameworkElement? what = null)
                    {
                        what ??= Root;
                        var rtb = new RenderTargetBitmap((int)Math.Ceiling(what.ActualWidth * 2), (int)Math.Ceiling(what.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
                        rtb.Render(what);
                        CaptureService.SavePng(rtb, System.IO.Path.Combine(dir, name + ".png"));
                    }
                    SetMode(Mode.Compact); await Task.Delay(1200);
                    ShowLevel("", L.T("Громкость"), 0.46, "volume");
                    await Task.Delay(500); Shot("1-volume");
                    ShowLevel("", L.T("Яркость"), 0.8, "brightness");
                    await Task.Delay(500); Shot("2-brightness");
                    await Task.Delay(1800);
                    _progress.Demo("explorer", 62); UpdateLive();
                    await Task.Delay(1500); Shot("3-progress");
                    _progress.Demo(null, 0);
                    _pinnedOpen = true; TabSettings.IsChecked = true; Expand();
                    ((System.Windows.Controls.RadioButton)SettingsNav.Children[0]).IsChecked = true;
                    await Task.Delay(1500); RefreshSettingsPage(); await Task.Delay(300);
                    ThemeEditorHost.BringIntoView(new Rect(0, -40, 10, 330));
                    await Task.Delay(700); Shot("4-theme-editor");
                    ((System.Windows.Controls.RadioButton)SettingsNav.Children[SettingsNav.Children.Count - 1]).IsChecked = true;
                    await Task.Delay(3500); Shot("5-self-check");
                    SetMode(Mode.Compact); _pinnedOpen = false; await Task.Delay(1000);
                    for (int step = 0; step < 3; step++)
                    {
                        ShowWelcome(step); await Task.Delay(900);
                        Shot("6-welcome-" + (step + 1), (FrameworkElement)_card!.Content);
                    }
                    ShowWhatsNew(args.ElementAtOrDefault(r + 3) ?? Updater.Current.ToString(3)); await Task.Delay(900);
                    Shot("7-whats-new", (FrameworkElement)_card!.Content);
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "activitytest")   // test: a job in another window's title, a blinking taskbar button, glass while visible to capture, the volume notice
                {
                    Settings.ReadOnly = true;
                    _settings.ShowProgress = true; _settings.MirrorNotifications = true; _settings.MirrorAllApps = true; _settings.ShowLevels = true;
                    _settings.Glass = true; _settings.CaptureHidden = false; ApplyCaptureExclusion(); ApplyGlass();
                    _progress.Finished += app => Log.Write($"activitytest: finished: {app}");
                    _titleTestProcess = "powershell";
                    await Task.Delay(2500);
                    CaptureFrost();
                    Log.Write($"activitytest: glass while visible to capture: mag works={MagCapture.Works}, frost covers={_frostCovers}, frost opacity={FrostHost.Opacity:0.0}");
                    Log.Write($"activitytest: flash fallback={FlashFallback}, shell hook={_shellHookMsg != 0}, volume device={(_volumeDeviceId.Length > 0)}");
                    if (_volumeDevice != null)
                    {
                        // a nudge of one step and straight back: does the notice come up?
                        var v = _volumeDevice.AudioEndpointVolume;
                        float was = v.MasterVolumeLevelScalar;
                        v.MasterVolumeLevelScalar = was > 0.02f ? was - 0.01f : was + 0.01f;
                        await Task.Delay(350);
                        Log.Write($"activitytest: volume notice shown={_keyKind == "volume" && _keyTimer.IsEnabled}");
                        v.MasterVolumeLevelScalar = was;
                    }
                    for (int i = 0; i < 18; i++)
                    {
                        await Task.Delay(1000);
                        Log.Write($"activitytest: job={(_progress.Current is { } j ? $"{j.App} {j.Percent}%" : "none")}, pill='{CompactTime.Text}', unread={_unread}, mode={_mode}");
                    }
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "customize")   // pictures of the hand-made layout (with --cover): size grips, the Layout page, reordered tabs, the mixer
                {
                    var dir = args.ElementAtOrDefault(r + 2) ?? System.IO.Path.GetTempPath();
                    Island.IsHitTestVisible = false; _settings.ExpandOnHover = false;
                    await Task.Delay(5000);
                    void Shot(string name)
                    {
                        var rtb = new RenderTargetBitmap((int)Math.Ceiling(Root.ActualWidth * 2), (int)Math.Ceiling(Root.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
                        rtb.Render(Root);
                        CaptureService.SavePng(rtb, System.IO.Path.Combine(dir, name + ".png"));
                    }
                    SetMode(Mode.Compact); await Task.Delay(1000);
                    EnterEditMode(); await Task.Delay(700); Shot("1-edit");
                    {
                        // pull the right grip 40 px and the bottom one 9 px, the way the mouse would
                        var from = CursorDip();
                        _testCursor = (from, new Vector(0, 0), DateTime.UtcNow);
                        _gripRight!.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0));
                        _testCursor = (from + new Vector(40, 0), new Vector(0, 0), DateTime.UtcNow);
                        _gripRight.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(40, 0));
                        _testCursor = (from, new Vector(0, 0), DateTime.UtcNow);
                        _gripBottom!.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0));
                        _testCursor = (from + new Vector(0, 9), new Vector(0, 0), DateTime.UtcNow);
                        _gripBottom.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(0, 9));
                        _testCursor = null;
                        Log.Write($"customize: grips: extra={_settings.PillExtra} (expect 80), scale={_settings.IslandScale} (expect 125)");
                    }
                    _settings.PillExtra = 140; _settings.IslandScale = 120; ApplyIslandScale(); MorphTo(new Size(CompactWidth, CompactHeight));
                    await Task.Delay(1500); Shot("2-edit-bigger");
                    Log.Write($"customize: edit mode={_editMode}, pill {Island.ActualWidth:0}x{Island.ActualHeight:0} at scale {UiScale}");
                    ResetPillSize(); ExitEditMode(); await Task.Delay(800);
                    _settings.PillClock = "timedate"; _settings.PillRight = "battery"; OnTick(); UpdatePillRight();
                    await Task.Delay(900); Shot("3-pill-date-battery");
                    _settings.PillClock = "time"; _settings.PillRight = "weather";
                    await DemoCommand("layoutpage"); await Task.Delay(1800); Shot("4-layout");
                    _settings.TabOrder = ["TabHome", "TabNotes", "TabMusic", "TabTimer", "TabClip", "TabSystem"]; _settings.HiddenTabs = ["TabChat", "TabCalendar", "TabWeather"];
                    ApplyTabLayout();
                    TabSystem.IsChecked = true; await Task.Delay(2500);
                    ShowTabIcons(true, false); await Task.Delay(600); Shot("5-system-mixer-tabs");
                    Log.Write($"customize: tabs shown={string.Join(",", PageTabs.Select(t => t.Name))}");
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "magstress")   // test: many glass grabs through the Magnification API, the size changing every time (it crashed on a resize)
                {
                    Settings.ReadOnly = true;
                    _settings.ExpandOnHover = false; _settings.Glass = false; _settings.CaptureHidden = false; ApplyCaptureExclusion(); ApplyGlass();
                    await Task.Delay(2000);
                    var tl = Island.PointToScreen(new Point(0, 0));
                    int n = secs > 0 ? secs : 1500, ok = 0;
                    var rnd = new Random(1);
                    Log.Write($"magstress: start, {n} grabs");
                    for (int i = 0; i < n; i++)
                    {
                        int w = i % 2 == 0 ? 200 + rnd.Next(60) : 700 + rnd.Next(500), h = i % 2 == 0 ? 40 + rnd.Next(20) : 300 + rnd.Next(400);
                        var rect = new System.Drawing.Rectangle((int)tl.X - w / 2, (int)tl.Y, w, h);
                        if (MagCapture.Capture(_hwnd, rect, Math.Max(1, w / 2), Math.Max(1, h / 2), null) != null) ok++;
                        if (i % 5 == 0) await Task.Delay(1);
                        if (i % 300 == 299 || i < 2) Log.Write($"magstress: {i + 1} done, {ok} ok, asked {w}x{h}, got {MagCapture.LastSize.W}x{MagCapture.LastSize.H}");
                    }
                    Log.Write($"magstress: finished without a crash, {ok}/{n} ok, works={MagCapture.Works}");
                    {
                        // is it the right piece of the screen? the same area both ways (the island hidden from the plain grab) must match
                        _settings.CaptureHidden = true; ApplyCaptureExclusion();
                        await Task.Delay(600);
                        foreach (var (cw, ch) in new[] { (240, 44), (900, 500) })
                        {
                            var rect = new System.Drawing.Rectangle((int)tl.X - cw / 2, (int)tl.Y + 4, cw, ch);
                            var plain = CaptureService.CaptureSmall(rect, cw / 2, ch / 2, null)!;
                            var copy = (byte[])plain.Clone();
                            var mag = MagCapture.Capture(_hwnd, rect, cw / 2, ch / 2, null)!;
                            double diff = 0; for (int i = 0; i < mag.Length; i += 4) diff += Math.Abs(mag[i] - copy[i]) + Math.Abs(mag[i + 1] - copy[i + 1]) + Math.Abs(mag[i + 2] - copy[i + 2]);
                            Log.Write($"magstress: same area {cw}x{ch}: mean difference {diff / (mag.Length / 4 * 3):0.0} of 255");
                        }
                    }
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "fpsapps")   // test: which full-screen programs get the FPS pill, in both modes (nothing is saved)
                {
                    Settings.ReadOnly = true;
                    _settings.ExpandOnHover = false;
                    await Task.Delay(2500);
                    // (process name, its full path): listed by name, by program path, by folder, and ones that are not listed
                    var gameDir = System.IO.Path.Combine("D:" + System.IO.Path.DirectorySeparatorChar, "Games");
                    (string Name, string Path)[] fronts =
                    [
                        ("chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe"), ("msedge", ""), ("Firefox", ""), ("browser", ""), ("explorer", @"C:\Windows\explorer.exe"), ("DynamicIsland", ""),
                        ("Cyberpunk2077", ""), ("game", System.IO.Path.Combine(gameDir, "Pirate", "game.exe")), ("launcher", System.IO.Path.Combine(gameDir, "Pirate", "launcher.exe")),
                        ("Witcher3", System.IO.Path.Combine(gameDir, "Witcher", "bin", "Witcher3.exe")), ("Witcher3", System.IO.Path.Combine(gameDir, "WitcherOld", "Witcher3.exe")),
                        ("eldenring", ""), ("", ""),
                    ];
                    _settings.GameStatsApps = ["Cyberpunk2077", System.IO.Path.Combine(gameDir, "Pirate", "game.exe"), System.IO.Path.Combine(gameDir, "Witcher")];
                    _settings.GameStats = true; _settings.HideInFullscreen = false; _testFullscreen = true;
                    var fgw = Native.GetForegroundWindow();
                    Native.GetWindowThreadProcessId(fgw, out uint fgPid);
                    Log.Write($"fpsapps: listed=[{string.Join(" | ", _settings.GameStatsApps)}]");
                    foreach (var only in new[] { false, true })
                    {
                        _settings.GameStatsOnlyListed = only;
                        foreach (var (n, path) in fronts)
                        {
                            // pretend the window in front belongs to a program with this name and path
                            _gamePid = fgPid; _gamePidName = n; _gamePidPath = path;
                            WatchFullscreen();
                            Log.Write($"fpsapps: only listed={only}, front='{n}' '{path}' -> counts as game={FpsMeter.CountsAsGame(n, path, only, _settings.GameStatsApps)}, FPS pill={_gameMode}, island hidden={_fullscreenHidden}");
                        }
                    }
                    _testFullscreen = false; _gamePid = 0; WatchFullscreen();
                    Log.Write($"fpsapps: no full-screen program -> FPS pill={_gameMode}, island hidden={_fullscreenHidden}");
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "bugfix")   // test: the game pill with "FPS in games" chosen, the FPS session starting and stopping, the Discord island while the main one opens
                {
                    Settings.ReadOnly = true;
                    _settings.ExpandOnHover = false;
                    await Task.Delay(2500);
                    foreach (var (stats, hide, full) in new[] { (true, false, true), (false, true, true), (false, false, true), (true, false, false) })
                    {
                        _settings.GameStats = stats; _settings.HideInFullscreen = hide; _testFullscreen = full;
                        WatchFullscreen();
                        Log.Write($"bugfix: FPS switch={stats}, hide switch={hide}, full-screen app={full} -> game pill={_gameMode}, hidden={_fullscreenHidden}, fps session running={_fps.Running}, denied={_fps.AccessDenied}");
                        await Task.Delay(1200);
                    }
                    _testFullscreen = null; _settings.GameStats = false; WatchFullscreen();
                    await Task.Delay(1500);

                    // the Discord island: how many times does it appear / disappear while the main island opens and folds?
                    _callProbeBusy = true;   // no real probing during the test
                    _discordInCall = true; _settings.DiscordSidePill = true;
                    await Task.Delay(1500);
                    int flips = 0; bool was = _callShown;
                    async Task Watch(int ms) { for (int i = 0; i < ms / 20; i++) { await Task.Delay(20); UpdateDiscordPill(); if (_callShown != was) { flips++; was = _callShown; if (flips <= 6) Log.Write($"bugfix:   flip -> {_callShown}: mode={_mode}, target w={_w.Target:0.0}, root={Root.ActualWidth:0.0}, pill={_callPill!.DesiredSize.Width:0.0}, scale={UiScale}"); } } }
                    Log.Write($"bugfix: call island shown while folded={_callShown}");
                    foreach (var tab in new[] { TabHome, TabSettings, TabSystem })
                    {
                        flips = 0;
                        tab.IsChecked = true; _pinnedOpen = true; Expand();
                        await Watch(1600);
                        Log.Write($"bugfix: opening {tab.Name}: call island changed {flips} time(s), shown at the end={_callShown}");
                        flips = 0;
                        _pinnedOpen = false; SetMode(Mode.Compact);
                        await Watch(1400);
                        Log.Write($"bugfix: folding back: changed {flips} time(s), shown at the end={_callShown}");
                    }
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "pulltest")   // pictures and numbers of the island reaching for the cursor (with --cover): the nearest point first, the body after it
                {
                    var dir = args.ElementAtOrDefault(r + 2) ?? System.IO.Path.GetTempPath();
                    Island.IsHitTestVisible = false; _settings.ExpandOnHover = false; _settings.Reactive = true; _settings.LiteGraphics = false;
                    await Task.Delay(5000);
                    SetMode(Mode.Compact); await Task.Delay(1200);
                    void Shot(string name)
                    {
                        var rtb = new RenderTargetBitmap((int)Math.Ceiling(Root.ActualWidth * 2), (int)Math.Ceiling(Root.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
                        rtb.Render(Root);
                        CaptureService.SavePng(rtb, System.IO.Path.Combine(dir, name + ".png"));
                    }
                    Shot("0-rest");
                    // the cursor at a spot given in island coordinates (pixels from its top-left corner)
                    void CursorAt(double ix, double iy)
                    {
                        var p = Island.PointToScreen(new Point(ix - _bodyX.Value, iy - _bodyY.Value));
                        _fakeCursor = new Native.POINT { X = (int)p.X, Y = (int)p.Y };
                    }
                    double w = Island.ActualWidth, h = Island.ActualHeight;
                    foreach (var (name, ix, iy) in new[] { ("below-right", w * 0.8, h + 34), ("right", w + 40, h / 2), ("below-left", w * 0.15, h + 26), ("above-left", -30.0, -4.0) })
                    {
                        CursorAt(ix, iy);
                        foreach (var ms in new[] { 90, 200, 450, 1300 })
                        {
                            await Task.Delay(ms == 90 ? 90 : ms == 200 ? 110 : ms == 450 ? 250 : 850);
                            CursorAt(ix, iy);
                            Log.Write($"pulltest: {name} +{ms} ms: lead=({_leadX.Value:0.0},{_leadY.Value:0.0}) body=({_bodyX.Value:0.0},{_bodyY.Value:0.0})");
                            if (ms is 200 or 1300) Shot($"{name}-{ms}");
                        }
                        _fakeCursor = new Native.POINT { X = -20000, Y = -20000 };
                        await Task.Delay(1500);
                        Log.Write($"pulltest: {name} released: lead=({_leadX.Value:0.0},{_leadY.Value:0.0}) body=({_bodyX.Value:0.0},{_bodyY.Value:0.0})");
                    }
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "charsheet")   // a picture of every character added in 1.8, in three moods (idle, music, asleep)
                {
                    var dir = args.ElementAtOrDefault(r + 2) ?? System.IO.Path.GetTempPath();
                    var ids = Views.CharacterView.All.Select(c => c.Id).SkipWhile(id => id != "dog").ToList();
                    const double k = 5, cellW = 30 * k + 16, cellH = 22 * k + 34;
                    var moods = new[] { Views.CharacterMood.Idle, Views.CharacterMood.Music, Views.CharacterMood.Sleep };
                    int cols = 8, rowsPerMood = (int)Math.Ceiling(ids.Count / (double)cols);
                    var canvas = new System.Windows.Controls.Canvas { Width = cols * cellW, Height = moods.Length * rowsPerMood * cellH, Background = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x14)) };
                    for (int m = 0; m < moods.Length; m++)
                        for (int i = 0; i < ids.Count; i++)
                        {
                            var ch = new Views.CharacterView { Placeholder = true, Kind = ids[i], Mood = moods[m], Beat = moods[m] == Views.CharacterMood.Music ? 0.6 : 0, LookX = 0.4, LookY = 0.2, LayoutTransform = new ScaleTransform(k, k) };
                            for (int t = 0; t < 14; t++) ch.Tick(0.05 + m * 0.013);
                            double x = (i % cols) * cellW + 8, y = (m * rowsPerMood + i / cols) * cellH + 6;
                            System.Windows.Controls.Canvas.SetLeft(ch, x); System.Windows.Controls.Canvas.SetTop(ch, y);
                            canvas.Children.Add(ch);
                            var label = new TextBlock { Text = ids[i] + (m == 1 ? " ♪" : m == 2 ? " z" : ""), Foreground = Brushes.Gray, FontSize = 13 };
                            System.Windows.Controls.Canvas.SetLeft(label, x); System.Windows.Controls.Canvas.SetTop(label, y + 22 * k + 4);
                            canvas.Children.Add(label);
                        }
                    canvas.Measure(new Size(canvas.Width, canvas.Height));
                    canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height));
                    canvas.UpdateLayout();
                    var rtb = new RenderTargetBitmap((int)canvas.Width, (int)canvas.Height, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(canvas);
                    CaptureService.SavePng(rtb, System.IO.Path.Combine(dir, "characters.png"));

                    // "no character": the pill without anyone, and a Discord call that must not show anywhere
                    Island.IsHitTestVisible = false; _settings.ExpandOnHover = false;
                    await Task.Delay(4000);
                    void Pill(string name)
                    {
                        var shot = new RenderTargetBitmap((int)Math.Ceiling(Root.ActualWidth * 2), 200, 192, 192, PixelFormats.Pbgra32);
                        shot.Render(Root);
                        CaptureService.SavePng(shot, System.IO.Path.Combine(dir, name + ".png"));
                    }
                    UseCharacter("fox"); await Task.Delay(900); Pill("pill-fox");
                    UseCharacter("none"); await Task.Delay(900); Pill("pill-none");
                    _callProbeBusy = true; _discordInCall = true;
                    _settings.ShowDiscordCall = false; _settings.DiscordSidePill = true; await Task.Delay(1200);
                    Log.Write($"charsheet: call hidden, side pill on -> island shown={_callShown}, in the pill={_liveIds.Contains("discord")}");
                    _settings.DiscordSidePill = false; await Task.Delay(1200);
                    Log.Write($"charsheet: call hidden, side pill off -> island shown={_callShown}, in the pill={_liveIds.Contains("discord")}");
                    _settings.ShowDiscordCall = true; await Task.Delay(1200);
                    Log.Write($"charsheet: call shown, side pill off -> island shown={_callShown}, in the pill={_liveIds.Contains("discord")}");
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "looktest")   // pictures of the look options (with --cover); without --cover: does the idle transparency come on?
                {
                    var dir = args.ElementAtOrDefault(r + 2) ?? System.IO.Path.GetTempPath();
                    if (_demoMedia == null)
                    {
                        Settings.ReadOnly = true;
                        _settings.IdleOpacity = 50; _settings.ExpandOnHover = false; ApplyLook();
                        await Task.Delay(3500);
                        Log.Write($"looktest: idle transparency 50%: dimmed={_idleDim}, island opacity={Island.Opacity:0.00}, hover delay timer={_expandDelay.Interval.TotalMilliseconds:0} ms");
                        _settings.IdleOpacity = 100; ApplyLook();
                        await Task.Delay(1500);
                        Log.Write($"looktest: idle transparency off: dimmed={_idleDim}, island opacity={Island.Opacity:0.00}");
                        Application.Current.Shutdown();
                        return;
                    }
                    Island.IsHitTestVisible = false; _settings.ExpandOnHover = false;
                    await Task.Delay(5000);
                    void Shot(string name)
                    {
                        var rtb = new RenderTargetBitmap((int)Math.Ceiling(Root.ActualWidth * 2), (int)Math.Ceiling(Root.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
                        rtb.Render(Root);
                        CaptureService.SavePng(rtb, System.IO.Path.Combine(dir, name + ".png"));
                    }
                    SetMode(Mode.Compact); await Task.Delay(1000);
                    _settings.Roundness = 0; _settings.ShowShadow = false; _settings.Clock12 = true; _settings.ClockSeconds = true; ApplyLook(); OnTick();
                    await Task.Delay(1200); Shot("1-square-noshadow-seconds");
                    _settings.Roundness = 45; _settings.ShowShadow = true; _settings.ClockSeconds = false; _settings.Clock12 = false; _settings.Font = "Georgia"; ApplyLook(); OnTick();
                    await Task.Delay(1200); Shot("2-round45-georgia");
                    TabHome.IsChecked = true; _pinnedOpen = true; Expand(); await Task.Delay(1800); Shot("3-open-round45-georgia");
                    _settings.Roundness = 100; _settings.Font = ""; ApplyLook();
                    await DemoCommand("layoutpage"); await Task.Delay(1800); Shot("4-layout-top");
                    SettingsScroll.ScrollToVerticalOffset(330); await Task.Delay(700); Shot("5-layout-look");
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "yieldtest")   // test: the island steps aside for a flying cursor, comes back on a rest, stays away in a game; a place for each program (a stand-in cursor, never the real one; with --cover)
                {
                    Settings.ReadOnly = true;
                    _settings.ExpandOnHover = false; _settings.YieldToCursor = true; _settings.HideInFullscreen = false; _settings.GameStats = false; _settings.HideInApps.Clear();
                    await Task.Delay(3500);
                    _testWindowUnder = true;
                    var c = Island.PointToScreen(new Point(Island.ActualWidth / 2, Island.ActualHeight / 2));
                    async Task Fly(Point from, Point to, int frames)
                    {
                        for (int i = 1; i <= frames; i++)
                        {
                            var q = from + (to - from) * ((double)i / frames);
                            _fakeCursor = new Native.POINT { X = (int)q.X, Y = (int)q.Y };
                            await Task.Delay(33);
                        }
                    }
                    bool Through() => (Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE) & 0x20) != 0;
                    string St() => $"yielding={_yield.Yielding}, clicks go through={Through()}, opacity {Root.Opacity:0.00}";
                    var far = new Point(c.X, c.Y + 500);
                    _fakeCursor = new Native.POINT { X = (int)far.X, Y = (int)far.Y }; await Task.Delay(300);
                    await Fly(far, c, 4); await Task.Delay(250);
                    Log.Write("yieldtest: flew in (480 px in 0.13 s): " + St());
                    await Task.Delay(500);
                    Log.Write("yieldtest: rested on it 0.5 s: " + St());
                    await Fly(c, far, 3); await Task.Delay(300);
                    await Fly(far, c, 40); await Task.Delay(250);
                    Log.Write("yieldtest: came slowly (1.3 s): " + St());
                    await Fly(c, far, 3); await Task.Delay(300);
                    _testFullscreen = true; await Task.Delay(400);
                    await Fly(far, c, 40); await Task.Delay(1200);
                    Log.Write($"yieldtest: in a full-screen game, came slowly and rested 1.2 s: front full screen={_fullscreenFront}, " + St());
                    await Fly(c, far, 3); await Task.Delay(400);
                    Log.Write("yieldtest: game, cursor gone: " + St());
                    _testFullscreen = false; _testWindowUnder = false;
                    await Fly(far, c, 4); await Task.Delay(250);
                    Log.Write("yieldtest: over the desktop, flew in: " + St());
                    _fakeCursor = new Native.POINT { X = -20000, Y = -20000 }; await Task.Delay(400);

                    // put away, opened or a notice shown while it makes way: never left see-through or letting clicks through
                    _testWindowUnder = true;
                    string Clear(string what) => $"yieldtest: {what}: {St()}, mode {_mode} -> {(!Through() && Root.Opacity > 0.95 && !_yield.Yielding ? "ok" : "FAIL")}";
                    async Task MakeWay()
                    {
                        _fakeCursor = new Native.POINT { X = (int)far.X, Y = (int)far.Y }; await Task.Delay(400);
                        await Fly(far, c, 4); await Task.Delay(220);
                        if (!_yield.Yielding) Log.Write("yieldtest: FAIL: it did not make way before this step");
                    }
                    await MakeWay(); HideIsland(animate: true); await Task.Delay(1500);
                    Log.Write(Clear("hidden while making way"));
                    _fakeCursor = new Native.POINT { X = (int)far.X, Y = (int)far.Y };
                    ShowIsland(); await Task.Delay(1500);
                    Log.Write(Clear("shown again"));
                    await MakeWay(); Expand(); await Task.Delay(900);
                    Log.Write(Clear("opened while making way"));
                    SetMode(Mode.Compact); await Task.Delay(1200);
                    await MakeWay(); ShowToast("\uE916", "Test", "test", null, ms: 1500); await Task.Delay(700);
                    Log.Write(Clear("a notice while making way"));
                    await Task.Delay(1800); if (_mode != Mode.Compact) SetMode(Mode.Compact); await Task.Delay(1200);
                    await MakeWay(); _settings.YieldToCursor = false; await Task.Delay(500);
                    Log.Write(Clear("switched off while making way"));
                    _settings.YieldToCursor = true;
                    _fakeCursor = new Native.POINT { X = (int)far.X, Y = (int)far.Y }; await Task.Delay(500);
                    // a screenshot hides it by Root's opacity: an animation that ended must not hold it visible
                    await HideForShot(); Log.Write($"yieldtest: hidden for a full screenshot after making way once: opacity {Root.Opacity:0.00} -> {(Root.Opacity < 0.05 ? "ok" : "FAIL")}"); Root.BeginAnimation(OpacityProperty, null); Root.Opacity = 1;

                    // every size of the island: its own edge counts
                    int size = _settings.IslandScale;
                    foreach (int pc in new[] { 80, 100, 130 })
                    {
                        _settings.IslandScale = pc; ApplyIslandScale(); await Task.Delay(1500);
                        var br = Island.PointToScreen(new Point(Island.ActualWidth, Island.ActualHeight / 2));
                        foreach (int off in new[] { -5, 15 })
                        {
                            var q = new Point(br.X + off, br.Y);
                            var away = new Point(q.X, q.Y + 500);
                            _fakeCursor = new Native.POINT { X = (int)away.X, Y = (int)away.Y }; await Task.Delay(400);
                            await Fly(away, q, 4); await Task.Delay(200);
                            Log.Write($"yieldtest: size {pc}%, {Math.Abs(off)} px {(off < 0 ? "inside" : "outside")} the right edge: yielding={_yield.Yielding} -> {(_yield.Yielding == off < 0 ? "ok" : "FAIL")}");
                        }
                    }
                    _fakeCursor = new Native.POINT { X = -20000, Y = -20000 };
                    _settings.IslandScale = size; ApplyIslandScale(); await Task.Delay(1500);
                    _testWindowUnder = false;

                    // put away at a program's place: the usual place stays the usual one; back at the place of whoever is in front
                    _settings.PlacePerApp = true; _testFrontApps = true;
                    double home0 = Left + Width / 2;
                    _settings.CenterX = home0; _settings.Top = Top;
                    _settings.AppPlaces["telegram"] = [home0 - 300, Top];
                    OnFrontApp("telegram"); await Task.Delay(1500);
                    HideIsland(animate: true); await Task.Delay(1200);
                    Log.Write($"yieldtest: hidden at a program's place: the usual place moved by {_settings.CenterX - home0:+0;-0;0} px -> {(Math.Abs(_settings.CenterX!.Value - home0) < 2 ? "ok" : "FAIL")}");
                    OnFrontApp("chrome"); ShowIsland(); await Task.Delay(1800);
                    Log.Write($"yieldtest: shown with another program in front: centre {Left + Width / 2 - home0:+0;-0;0} px from usual -> {(Math.Abs(Left + Width / 2 - home0) < 3 ? "ok" : "FAIL")}");
                    _settings.AppPlaces.Remove("telegram");
                    OnFrontApp(null); await Task.Delay(1500);

                    // a place for each program
                    _settings.PlacePerApp = true; _testFrontApps = true;
                    double usual = Left + Width / 2;
                    _settings.AppPlaces["telegram"] = [usual - 300, Top];
                    OnFrontApp("telegram"); await Task.Delay(1500);
                    Log.Write($"yieldtest: Telegram in front: centre {Left + Width / 2 - usual:+0;-0} px from usual, owner={_placeOwner}");
                    OnFrontApp("chrome"); await Task.Delay(1500);
                    Log.Write($"yieldtest: Chrome in front (no place of its own): centre {Left + Width / 2 - usual:+0;-0} px, owner={_placeOwner ?? "usual"}");
                    OnFrontApp("telegram"); await Task.Delay(1500);
                    _px.Snap(Left); _px.Target = Left + 120; _posActive = true; StartRendering(); await Task.Delay(1500);   // as if dragged further while Telegram is in front
                    Log.Write($"yieldtest: moved 120 px more with Telegram in front: its place now {_settings.AppPlaces["telegram"][0] - usual:+0;-0} px, the usual place {(_settings.CenterX ?? SystemParameters.PrimaryScreenWidth / 2) - usual:+0;-0} px");
                    AlignIsland(); await Task.Delay(1500);
                    Log.Write($"yieldtest: 'align' with Telegram in front: its place forgotten={!_settings.AppPlaces.ContainsKey("telegram")}, centre {Left + Width / 2 - usual:+0;-0} px");
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "scrubtest")   // test: the scrubber — press jumps and wakes the capsule, steps of 5, release puts it to sleep, a set value eases over 150 ms; pictures of the bar only (with --cover)
                {
                    var dir = args.ElementAtOrDefault(r + 2) ?? System.IO.Path.GetTempPath();
                    Settings.ReadOnly = true;
                    Island.IsHitTestVisible = false; _settings.ExpandOnHover = false;
                    await Task.Delay(3500);
                    _pinnedOpen = true; Expand(); await Task.Delay(600);
                    TabSettings.IsChecked = true; await Task.Delay(800);
                    foreach (RadioButton nav in SettingsNav.Children)
                        if (SettingsSections.Children[SettingsNav.Children.IndexOf(nav)] is FrameworkElement sec && sec.IsAncestorOf(ScaleSlider)) nav.IsChecked = true;
                    await Task.Delay(900);
                    var b = ScaleSlider;
                    int shot = 0;
                    void Shot(string what)
                    {
                        double w = b.ActualWidth + 24, h = b.ActualHeight + 24;
                        var off = VisualTreeHelper.GetOffset(b);
                        var own = new RenderTargetBitmap((int)((off.X + w) * 2), (int)((off.Y + h) * 2), 192, 192, PixelFormats.Pbgra32);
                        own.Render(b);
                        var dv = new DrawingVisual();
                        using (var dc = dv.RenderOpen())
                        {
                            dc.DrawRectangle((Brush)FindResource("IslandBg"), null, new Rect(0, 0, w, h));
                            dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));
                            dc.DrawImage(own, new Rect(12 - off.X, 12 - off.Y, off.X + w, off.Y + h));
                            dc.Pop();
                        }
                        var rtb = new RenderTargetBitmap((int)(w * 2), (int)(h * 2), 192, 192, PixelFormats.Pbgra32);
                        rtb.Render(dv);
                        CaptureService.SavePng(rtb, System.IO.Path.Combine(dir, $"scrub-{++shot}-{what}.png"));
                    }
                    string St() { var t = b.TestState; return $"value {b.Value:0.##} '{b.Format(b.Value)}', fill {t.Shown:0.000}, capsule awake {t.Wake:0.00}, dragging={t.Dragging}, moving={t.Moving}"; }
                    Log.Write($"scrubtest: '{b.Label}' {b.ActualWidth:0}x{b.ActualHeight:0}, at rest: " + St()); Shot("rest");
                    b.TestPress(0.37 * b.ActualWidth);
                    await Task.Delay(60); Log.Write("scrubtest: pressed at 37%, 0.06 s: " + St());
                    await Task.Delay(300); Log.Write("scrubtest: 0.36 s: " + St()); Shot("pressed");
                    b.TestDrag(0.83 * b.ActualWidth); await Task.Delay(20); Log.Write("scrubtest: dragged to 83%: " + St()); Shot("dragged");
                    b.TestDrag(b.ActualWidth + 50); Log.Write("scrubtest: dragged past the end: " + St());
                    b.TestRelease();
                    await Task.Delay(400); Log.Write("scrubtest: released, 0.4 s: " + St()); Shot("released");
                    b.Value = 90;
                    await Task.Delay(50); Log.Write("scrubtest: set to 90, 0.05 s (easing): " + St());
                    await Task.Delay(250); Log.Write("scrubtest: 0.3 s: " + St()); Shot("eased");
                    b.Value = 100;
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "plantest")   // test: Claude Code's plan from hook bodies (TaskCreate / TaskUpdate, then TodoWrite) — the rows' states, check, dimming; pictures of the plan only (with --cover)
                {
                    var dir = args.ElementAtOrDefault(r + 2) ?? System.IO.Path.GetTempPath();
                    Settings.ReadOnly = true;
                    Island.IsHitTestVisible = false; _settings.ExpandOnHover = false;
                    await Task.Delay(3500);
                    _pinnedOpen = true; Expand(); await Task.Delay(900);
                    var plan = new ClaudePlan();
                    int shot = 0;
                    void Feed(string body)
                    {
                        var snap = plan.Apply((System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(body)!, "DynamicIsland");
                        Log.Write("plantest: hook " + (snap == null ? "ignored" : $"→ {snap.Tasks.Count} tasks: " + string.Join(", ", snap.Tasks.Select(t => $"#{t.Id} {t.Status}"))));
                        if (snap != null) ShowClaudePlan(snap);
                    }
                    void Shot(string what)
                    {
                        var el = ClaudePlanTile;
                        el.UpdateLayout();
                        var off = VisualTreeHelper.GetOffset(el);
                        double w = el.ActualWidth + 20, h = el.ActualHeight + 20, fw = off.X + w, fh = off.Y + h;
                        var own = new RenderTargetBitmap((int)(fw * 2), (int)(fh * 2), 192, 192, PixelFormats.Pbgra32);
                        own.Render(el);
                        var dv = new DrawingVisual();
                        using (var dc = dv.RenderOpen())
                        {
                            dc.DrawRectangle((Brush)FindResource("IslandBg"), null, new Rect(0, 0, w, h));
                            dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));
                            dc.DrawImage(own, new Rect(10 - off.X, 10 - off.Y, fw, fh));
                            dc.Pop();
                        }
                        var rtb = new RenderTargetBitmap((int)(w * 2), (int)(h * 2), 192, 192, PixelFormats.Pbgra32);
                        rtb.Render(dv);
                        CaptureService.SavePng(rtb, System.IO.Path.Combine(dir, $"plan-{++shot}-{what}.png"));
                    }
                    string St() { var t = ClaudePlanList.TestState; return $"{t.Count}: " + string.Join(" | ", t.Rows.Select(x => $"'{x.Text}' {x.State} o {x.Opacity:0.00} y {x.Y:0.00} tint {x.Tint:0.00} check {x.Check:0.00}{(x.Streak ? " streak" : "")}")); }
                    const string S = "\"session_id\":\"s1\",\"hook_event_name\":\"PostToolUse\",\"cwd\":\"C:\\\\src\\\\DynamicIsland\"";
                    Feed("{" + S + ",\"tool_name\":\"Edit\",\"tool_input\":{\"file_path\":\"a.cs\"}}");
                    Feed("{" + S + ",\"tool_name\":\"TaskCreate\",\"tool_input\":{\"subject\":\"Найти, где тормозит стекло\",\"activeForm\":\"Ищу, где тормозит стекло\"},\"tool_response\":{\"task\":{\"id\":\"1\",\"subject\":\"x\"}}}");
                    Feed("{" + S + ",\"tool_name\":\"TaskCreate\",\"tool_input\":{\"subject\":\"Перенести захват в фоновый поток\",\"active_form\":\"Переношу захват в фон\"},\"tool_response\":{\"task\":{\"id\":\"2\"}}}");
                    Feed("{" + S + ",\"tool_name\":\"TaskCreate\",\"tool_input\":{\"subject\":\"Собрать и проверить\"},\"tool_response\":\"Task #3 created successfully: Собрать и проверить\"}");
                    Feed("{" + S + ",\"tool_name\":\"TaskCreate\",\"tool_input\":{\"subject\":\"Записать в CHANGELOG\"}}");
                    await Task.Delay(300); Log.Write("plantest: four pending: " + St()); Shot("pending");
                    Feed("{" + S + ",\"tool_name\":\"TaskUpdate\",\"tool_input\":{\"taskId\":\"1\",\"status\":\"in_progress\"}}");
                    await Task.Delay(500); Log.Write("plantest: first running: " + St()); Shot("running");
                    Feed("{" + S + ",\"tool_name\":\"TaskUpdate\",\"tool_input\":{\"id\":\"1\",\"status\":\"completed\"}}");
                    Feed("{" + S + ",\"tool_name\":\"TaskUpdate\",\"tool_input\":{\"task_id\":\"2\",\"status\":\"in_progress\"}}");
                    await Task.Delay(60); Log.Write("plantest: 0.06 s after the first was done: " + St()); Shot("checking");
                    await Task.Delay(540); Log.Write("plantest: 0.6 s after: " + St()); Shot("done1");
                    Feed("{" + S + ",\"tool_name\":\"TaskUpdate\",\"tool_input\":{\"taskId\":\"4\",\"status\":\"deleted\"}}");
                    Feed("{" + S + ",\"tool_name\":\"TaskUpdate\",\"tool_input\":{\"taskId\":\"2\",\"status\":\"completed\"}}");
                    Feed("{" + S + ",\"tool_name\":\"TaskUpdate\",\"tool_input\":{\"taskId\":\"3\",\"status\":\"in_progress\"}}");
                    await Task.Delay(600); Log.Write("plantest: #4 deleted, #2 done, #3 running: " + St()); Shot("later");
                    Feed("{\"session_id\":\"s2\",\"hook_event_name\":\"PostToolUse\",\"tool_name\":\"TodoWrite\",\"tool_input\":{\"todos\":[{\"content\":\"Обновить перевод\",\"status\":\"completed\",\"activeForm\":\"Обновляю перевод\"},{\"content\":\"Проверить сборку\",\"status\":\"in_progress\",\"activeForm\":\"Проверяю сборку\"}]}}");
                    await Task.Delay(500); Log.Write("plantest: new session with TodoWrite: " + St()); Shot("todowrite");
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "sidetoast")   // test (2.0.1): a notice while the island is open goes beside it; the island stays open; a real click on it does what the notice does
                {
                    Settings.ReadOnly = true;
                    _settings.ExpandOnHover = false;
                    await Task.Delay(3000);
                    _pinnedOpen = true; Expand(); await Task.Delay(1200);
                    double h0 = Island.Height;
                    bool clicked = false;
                    ShowToast("\uE8BD", "Telegram", L.T("Новое сообщение"), () => clicked = true, new SolidColorBrush(Color.FromRgb(0x2A, 0xAB, 0xEE)), ms: 6000);
                    await Task.Delay(600);
                    var tl = Island.PointToScreen(new Point(0, 0)); var br = Island.PointToScreen(new Point(Island.ActualWidth, 0));
                    var st = _sideToast?.TestState;
                    double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
                    Log.Write($"sidetoast: island {_mode}, height {h0:0}→{Island.Height:0}; side notice shown={st?.Shown}, '{st?.Title}', at x={st?.Left * dpi:0} (island {tl.X:0}…{br.X:0}) -> " +
                              (_mode == Mode.Expanded && st?.Shown == true && (st.Value.Left * dpi >= br.X - 1 || st.Value.Left * dpi + 50 <= tl.X) ? "ok" : "FAIL"));
                    // a real click on it
                    var p = _sideToast!.PointToScreen(new Point(_sideToast.ActualWidth / 2, _sideToast.ActualHeight / 2));
                    Native.SetCursorPos((int)p.X, (int)p.Y); await Task.Delay(300);
                    var under = Native.WindowFromPoint(new Native.POINT { X = (int)p.X, Y = (int)p.Y });
                    Log.Write($"sidetoast: at {p.X:0},{p.Y:0} the window under the cursor is {(under == new System.Windows.Interop.WindowInteropHelper(_sideToast).Handle ? "the side notice" : under == _hwnd ? "the island" : "another (" + under + ")")}");
                    Native.MouseClick(); await Task.Delay(600);
                    Log.Write($"sidetoast: clicked: action ran={clicked}, side notice shown={_sideToast.TestState.Shown}, island {_mode} -> {(clicked && !_sideToast.TestState.Shown ? "ok" : "FAIL")}");
                    // folded island: the notice is the island's own, as before
                    _sideToast.Hide(animate: false);
                    _pinnedOpen = false; SetMode(Mode.Compact); await Task.Delay(900);
                    ShowToast("\uE916", L.T("Таймер"), "test", null); await Task.Delay(400);
                    Log.Write($"sidetoast: folded island: mode {_mode}, side notice shown={_sideToast.TestState.Shown} -> {(_mode == Mode.Toast && !_sideToast.TestState.Shown ? "ok" : "FAIL")}");
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "stackmouse")   // test (2.0.1): the notification stack with the real mouse: hover, close five cards with their ×, Clear all; the island's height every frame
                {
                    Settings.ReadOnly = true;
                    _settings.ExpandOnHover = false;
                    await Task.Delay(3000);
                    for (int i = 0; i < 5; i++) HomeMissed.Add(new("\uE916", null, "Card " + i, "a message with some text in it", DateTime.Now, null));
                    _pinnedOpen = true; Expand(); TabHome.IsChecked = true; await Task.Delay(1200);
                    var frames = new List<(double T, double H, Mode M)>();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    void OnFrame(object? s, EventArgs e) => frames.Add((sw.Elapsed.TotalMilliseconds, Island.Height, _mode));
                    CompositionTarget.Rendering += OnFrame;
                    Native.POINT Screen(FrameworkElement el, double fx, double fy)
                    {
                        var p = el.PointToScreen(new Point(el.ActualWidth * fx, el.ActualHeight * fy));
                        return new Native.POINT { X = (int)p.X, Y = (int)p.Y };
                    }
                    async Task MoveTo(Native.POINT to, int steps = 12)
                    {
                        Native.GetCursorPos(out var from);
                        for (int i = 1; i <= steps; i++) { Native.SetCursorPos(from.X + (to.X - from.X) * i / steps, from.Y + (to.Y - from.Y) * i / steps); await Task.Delay(12); }
                    }
                    async Task Click() { Native.MouseClick(); await Task.Delay(30); }
                    string Since(double from)
                    {
                        var f = frames.Where(x => x.T >= from).ToList();
                        if (f.Count == 0) return "no frames";
                        int changes = 0; for (int i = 1; i < f.Count; i++) if (Math.Abs(f[i].H - f[i - 1].H) > 0.5) changes++;
                        return $"height {f.Min(x => x.H):0}…{f.Max(x => x.H):0} px, changed in {changes} of {f.Count} frames, mode {string.Join("/", f.Select(x => x.M).Distinct())}";
                    }
                    // onto the stack: it fans out
                    var first = HomeMissed.TestCard(0)!.Value;
                    await MoveTo(Screen(first.Face, 0.5, 0.5)); await Task.Delay(1200);
                    _pinnedOpen = false;   // like a real hover-open: from now on leaving the island folds it
                    Log.Write($"stackmouse: hover the stack: {Since(0)}");
                    for (int k = 0; k < 4; k++)
                    {
                        double t0 = sw.Elapsed.TotalMilliseconds;
                        var card = HomeMissed.TestCard(0)!.Value;
                        await MoveTo(Screen(card.Face, 0.3, 0.5), 6); await Task.Delay(250);   // hover the card: its × shows
                        await MoveTo(Screen(card.Close, 0.5, 0.5), 6); await Task.Delay(150);
                        await Click(); await Task.Delay(900);
                        Log.Write($"stackmouse: close #{k + 1} with ×: {Since(t0)}, {HomeMissed.Count} left");
                    }
                    double tl = sw.Elapsed.TotalMilliseconds;
                    var last = HomeMissed.TestCard(0)!.Value;
                    await MoveTo(Screen(last.Face, 0.3, 0.5), 6); await Task.Delay(250);
                    await MoveTo(Screen(last.Close, 0.5, 0.5), 6); await Task.Delay(150);
                    await Click(); await Task.Delay(900);
                    Log.Write($"stackmouse: close the last one: {Since(tl)}, {HomeMissed.Count} left");
                    double ta = sw.Elapsed.TotalMilliseconds;
                    await MoveTo(Screen(TabHome, 0.5, 0.5), 10); await Task.Delay(1200);
                    Log.Write($"stackmouse: cursor up to the tabs: {Since(ta)}");
                    CompositionTarget.Rendering -= OnFrame;
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "livecheck")   // test (2.0.1): page dots vs tab icons, closing cards under the cursor, the command bar's shortcut (nothing is saved)
                {
                    Settings.ReadOnly = true;
                    Island.IsHitTestVisible = false; _settings.ExpandOnHover = false;
                    await Task.Delay(3000);
                    _pinnedOpen = true; Expand(); await Task.Delay(900);
                    string Dots() => string.Join(",", DotsRow.Children.OfType<UIElement>().Select(d => d.Opacity.ToString("0.0")));
                    string Icons() => string.Join(",", PageTabs.Select(t => t.Opacity.ToString("0.0")));
                    bool Overlap() => DotsRow.Children.OfType<UIElement>().Any(d => d.Opacity > 0.05) && PageTabs.Any(t => t.Opacity > 0.05);
                    void Report(string what)
                    {
                        Log.Write($"livecheck: dots {what}: dots [{Dots()}], icons [{Icons()}] -> {(Overlap() ? "FAIL: dots over icons" : "ok")}");
                        // the pill under the tabs sits on the chosen tab (its layout place, not a frame of its scale animation)
                        var chosen = PageTabs.FirstOrDefault(t => t.IsChecked == true);
                        if (chosen == null || chosen.Opacity < 0.5) return;
                        var at = VisualTreeHelper.GetOffset(chosen);
                        var pill = TabStrip.TestState;
                        double off = Math.Max(Math.Abs(pill.From - at.X), Math.Abs(pill.To - (at.X + chosen.ActualWidth)));
                        Log.Write($"livecheck: pill {what}: {pill.From:0}…{pill.To:0}{(pill.Moving ? " (moving)" : "")}, tab {at.X:0}…{at.X + chosen.ActualWidth:0} -> {(off <= 2 ? "ok" : pill.Moving ? "still on its way" : $"FAIL: off by {off:0} px")}");
                    }
                    // A5: icons always, then the layout changed (a tab hidden, the order changed), then shown again
                    var mode = _settings.TabIcons; var hidden = _settings.HiddenTabs.ToList(); var order = _settings.TabOrder.ToList();
                    _settings.TabIcons = "always"; ApplyTabIconsMode(); await Task.Delay(400); Report("always, start");
                    _settings.HiddenTabs = [.. hidden, "TabWeather"]; ApplyTabLayout(); await Task.Delay(400); Report("always, a tab hidden");
                    _settings.TabOrder = [.. OrderedTabs().Select(t => t.Name).Reverse()]; ApplyTabLayout(); await Task.Delay(400); Report("always, order reversed");
                    await Task.Delay(1500); Report("always, order reversed, 2 s later");
                    _settings.HiddenTabs = hidden; _settings.TabOrder = order; ApplyTabLayout(); await Task.Delay(400); Report("always, back");
                    await Task.Delay(1500); Report("always, back, 2 s later");
                    _settings.TabIcons = "hover"; ApplyTabIconsMode(); await Task.Delay(400); Report("hover, mouse away");
                    _settings.HiddenTabs = [.. hidden, "TabWeather"]; ApplyTabLayout(); await Task.Delay(400);
                    ShowTabIcons(true, animate: true); await Task.Delay(700); Report("hover, layout changed while folded, then the mouse came");
                    _settings.HiddenTabs = hidden; ApplyTabLayout(); ShowTabIcons(false, animate: false); await Task.Delay(400);
                    ShowTabIcons(true, animate: true); await Task.Delay(600); Report("hover, mouse on the tabs");
                    _settings.HiddenTabs = [.. hidden, "TabWeather"]; ApplyTabLayout(); await Task.Delay(400); Report("hover, mouse on the tabs, a tab hidden");
                    ShowTabIcons(false, animate: true); await Task.Delay(600); Report("hover, mouse left");
                    _settings.HiddenTabs = hidden; ApplyTabLayout(); _settings.TabIcons = mode; ApplyTabIconsMode();
                    // A7: five cards, closed one by one under the cursor, then Clear all; the island must neither fold nor shrink
                    TabHome.IsChecked = true; await Task.Delay(400);
                    for (int i = 0; i < 5; i++) HomeMissed.Add(new("", null, "Card " + i, "body", DateTime.Now, null));
                    HomeMissed.TestHover(true); await Task.Delay(900);
                    double before = Island.Height;
                    var heights = new List<double>();
                    for (int i = 0; i < 4; i++) { HomeMissed.TestClose(0); await Task.Delay(500); heights.Add(Island.Height); }
                    Log.Write($"livecheck: cards: island {before:0} px with 5 cards, after each close [{string.Join(", ", heights.Select(h => h.ToString("0")))}], mode {_mode} -> {(heights.All(h => h >= before - 1) && _mode == Mode.Expanded ? "ok" : "FAIL")}");
                    HomeMissed.ClearAll(); await Task.Delay(600);
                    Log.Write($"livecheck: clear all under the cursor: island {Island.Height:0} px, {HomeMissed.Count} cards, mode {_mode} -> {(Island.Height >= before - 1 && _mode == Mode.Expanded ? "ok" : "FAIL")}");
                    HomeMissed.TestHover(false); await Task.Delay(900);
                    Log.Write($"livecheck: cursor left the stack: island {Island.Height:0} px (shrank once: {(Island.Height < before - 1 ? "ok" : "FAIL")}), stack {HomeMissed.Visibility}");
                    // A2: each choice registered at once, the others let go (Ctrl+Shift+Space is the probe: free unless chosen)
                    var keys = _settings.CommandHotkey;
                    foreach (var k in CommandHotkeys.Select(h => h.Name).Append(""))
                    {
                        _settings.CommandHotkey = k;
                        bool ok = RegisterCommandHotkey();
                        bool probeFree = Native.RegisterHotKey(IntPtr.Zero, 99, Native.MOD_CONTROL | 0x4 | Native.MOD_NOREPEAT, 0x20);
                        if (probeFree) Native.UnregisterHotKey(IntPtr.Zero, 99);
                        bool want = k != "Ctrl+Shift+Space";
                        Log.Write($"livecheck: command bar shortcut '{(k == "" ? "off" : k)}': registered={ok}, Ctrl+Shift+Space free for others={probeFree} -> {(probeFree == want ? "ok" : "FAIL")}");
                    }
                    _settings.CommandHotkey = keys; RegisterCommandHotkey();
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "stacktest")   // test: the notification stack — folded, fanned out, a card swiped away and one sprung back; pictures of the stack only (demo items, with --cover)
                {
                    var dir = args.ElementAtOrDefault(r + 2) ?? System.IO.Path.GetTempPath();
                    Settings.ReadOnly = true;
                    Island.IsHitTestVisible = false; _settings.ExpandOnHover = false;
                    await Task.Delay(3500);
                    var st = HomeMissed;
                    st.Add(new("\uE916", (Brush)FindResource("Accent"), L.T("Таймер"), L.T("Время вышло: 25:00"), DateTime.Now.AddMinutes(-42), null));
                    st.Add(new("\uE896", (Brush)FindResource("AccentBlue"), L.T("Загрузка завершена"), "atoll-2.0-setup.exe", DateTime.Now.AddMinutes(-17), null));
                    st.Add(new("✳", (Brush)FindResource("ClaudeOrange"), "Claude Code", "DynamicIsland · " + L.T("готово"), DateTime.Now.AddMinutes(-3), null));
                    st.Add(new("\uE8BD", new SolidColorBrush(Color.FromRgb(0x2A, 0xAB, 0xEE)), "Telegram", L.T("Новое сообщение"), DateTime.Now, null));
                    _pinnedOpen = true; Expand(); await Task.Delay(1200);
                    int shot = 0;
                    void Shot(string what)
                    {
                        st.UpdateLayout();
                        double w = st.ActualWidth + 40, h = st.ActualHeight + 16;
                        var dv = new DrawingVisual();
                        using (var dc = dv.RenderOpen())
                        {
                            dc.DrawRectangle((Brush)FindResource("IslandBg"), null, new Rect(0, 0, w, h));
                            // RenderTargetBitmap draws the stack at its offset in the page: render it big enough, then shift it back
                            var off = VisualTreeHelper.GetOffset(st);
                            double fw = off.X + st.ActualWidth + 20, fh = off.Y + st.ActualHeight + 20;
                            var own = new RenderTargetBitmap((int)(fw * 2), (int)(fh * 2), 192, 192, PixelFormats.Pbgra32);
                            own.Render(st);
                            dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));
                            dc.DrawImage(own, new Rect(20 - off.X, 10 - off.Y, fw, fh));
                            dc.Pop();
                        }
                        var rtb = new RenderTargetBitmap((int)(w * 2), (int)(h * 2), 192, 192, PixelFormats.Pbgra32);
                        rtb.Render(dv);
                        CaptureService.SavePng(rtb, System.IO.Path.Combine(dir, $"stack-{++shot}-{what}.png"));
                    }
                    string St()
                    {
                        var t = st.TestState;
                        return $"height {t.ListHeight:0}, open={t.Open}, island {ActualHeight:0} px; cards: " + string.Join(" | ", t.Cards.Select(c => $"y {c.Y:0} s {c.Scale:0.00} o {c.Opacity:0.00} x {c.X:0}{(c.Leaving ? " leaving" : "")}"));
                    }
                    Log.Write("stacktest: folded: " + St()); Shot("folded");
                    st.TestHover(true);
                    await Task.Delay(120); Log.Write("stacktest: 0.12 s into fanning out: " + St()); Shot("fanning");
                    await Task.Delay(600); Log.Write("stacktest: fanned out: " + St()); Shot("open");
                    st.TestSwipe(1, 60, 0.02);
                    await Task.Delay(100); Log.Write("stacktest: 60 px slow swipe, 0.1 s later: " + St());
                    await Task.Delay(500); Log.Write("stacktest: sprung back: " + St());
                    st.TestSwipe(1, 70, 0.6);
                    await Task.Delay(90); Log.Write("stacktest: 70 px flick, 0.09 s later: " + St()); Shot("flying");
                    await Task.Delay(700); Log.Write("stacktest: after the flick: " + St() + $", {st.Count} left"); Shot("after");
                    st.TestHover(false);
                    await Task.Delay(700); Log.Write("stacktest: folded again: " + St()); Shot("refolded");
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "menutest2")   // test: the Dynamic menu — the island menu from the ☰ button grows out of a pill, items rise in; the cross; pictures of the menu only (with --cover)
                {
                    var dir = args.ElementAtOrDefault(r + 2) ?? System.IO.Path.GetTempPath();
                    Settings.ReadOnly = true;
                    Island.IsHitTestVisible = false; _settings.ExpandOnHover = false;
                    _settings.MenuButton = true; ApplyLook();
                    await Task.Delay(3500);
                    _pinnedOpen = true; Expand(); await Task.Delay(900);
                    Log.Write($"menutest2: button visible={MenuButton.IsVisible}, {MenuButton.ActualWidth:0}x{MenuButton.ActualHeight:0}");
                    int shot = 0;
                    void Shot(string what)
                    {
                        if (PresentationSource.FromVisual(IslandMenu) is not System.Windows.Interop.HwndSource src || src.RootVisual is not FrameworkElement root) return;
                        var s = Views.MenuMorph.Last;
                        if (s.FullW <= 0) return;
                        double w = s.FullW + 40, h = s.FullH + 40;
                        var dv = new DrawingVisual();
                        using (var dc = dv.RenderOpen())
                        {
                            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x5A, 0x6E, 0x8C)), null, new Rect(0, 0, w, h));
                            var shape = new Rect(20 + (s.Right ? s.FullW - s.W : 0), 20 + (s.Bottom ? s.FullH - s.H : 0), s.W, s.H);
                            dc.PushClip(new RectangleGeometry(shape, s.R, s.R));
                            dc.DrawRectangle((Brush)FindResource("MenuBg"), null, shape);
                            var own = new RenderTargetBitmap((int)(s.FullW * 2), (int)(s.FullH * 2), 192, 192, PixelFormats.Pbgra32);
                            own.Render(root);
                            dc.DrawImage(own, new Rect(20, 20, s.FullW, s.FullH));
                            dc.Pop();
                        }
                        var rtb = new RenderTargetBitmap((int)(w * 2), (int)(h * 2), 192, 192, PixelFormats.Pbgra32);
                        rtb.Render(dv);
                        CaptureService.SavePng(rtb, System.IO.Path.Combine(dir, $"menu-{++shot}-{what}.png"));
                    }
                    string St() { var s = Views.MenuMorph.Last; return $"shape {s.W:0}x{s.H:0} r{s.R:0.0} of {s.FullW:0}x{s.FullH:0}, first item {s.Item:0.00}, from {(s.Bottom ? "bottom" : "top")}-{(s.Right ? "right" : "left")}"; }
                    MenuButton_Click(MenuButton, new RoutedEventArgs());
                    foreach (var ms in new[] { 40, 80, 120, 200, 400, 900 })
                    {
                        await Task.Delay(ms - (shot == 0 ? 0 : new[] { 40, 80, 120, 200, 400, 900 }[shot - 1]));
                        Shot($"{ms}ms"); Log.Write($"menutest2: {ms} ms: " + St() + $", menu open={IslandMenu.IsOpen}");
                    }
                    IslandMenu.IsOpen = false;
                    await Task.Delay(500);
                    Log.Write($"menutest2: closed, menu open={IslandMenu.IsOpen}, cross={MenuToggle.Open}");
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "copytest")   // test: the copy button's roll to "Copied" and back, the width fitting; pictures (the clipboard is not touched; with --cover)
                {
                    var dir = args.ElementAtOrDefault(r + 2) ?? System.IO.Path.GetTempPath();
                    Settings.ReadOnly = true;
                    Island.IsHitTestVisible = false; _settings.ExpandOnHover = false;
                    await Task.Delay(3500);
                    _pinnedOpen = true; Expand(); await Task.Delay(600);
                    TabSettings.IsChecked = true; await Task.Delay(800);
                    foreach (RadioButton nav in SettingsNav.Children)
                        if (SettingsSections.Children[SettingsNav.Children.IndexOf(nav)] is FrameworkElement sec && sec.IsAncestorOf(PerfReportButton)) nav.IsChecked = true;
                    await Task.Delay(900);
                    var b = (Views.CopyButton)PerfReportButton;
                    int shot = 0;
                    void Shot(string what)
                    {
                        double w = b.ActualWidth + 60, h = b.ActualHeight + 16;
                        var dv = new DrawingVisual();
                        using (var dc = dv.RenderOpen())
                        {
                            dc.DrawRectangle((Brush)FindResource("IslandBg"), null, new Rect(0, 0, w, h));
                            dc.DrawRectangle(new VisualBrush(b) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(8, 8, b.ActualWidth, b.ActualHeight));
                        }
                        var rtb = new RenderTargetBitmap((int)(w * 2), (int)(h * 2), 192, 192, PixelFormats.Pbgra32);
                        rtb.Render(dv);
                        CaptureService.SavePng(rtb, System.IO.Path.Combine(dir, $"copy-{++shot}-{what}.png"));
                    }
                    string St() { var s = b.TestState; return $"roll {s.Roll:0.00}, icon {s.Icon:0.00}, word width {s.Width:0}, button {b.ActualWidth:0} px, moving={s.Moving}"; }
                    Log.Write($"copytest: at rest: '{b.Label}' " + St()); Shot("rest");
                    b.Done();
                    await Task.Delay(110); Shot("rolling"); Log.Write("copytest: 0.11 s after: " + St());
                    await Task.Delay(700); Shot("copied"); Log.Write("copytest: 0.8 s after: " + St());
                    await Task.Delay(2000); Shot("back"); Log.Write("copytest: 2.8 s after: " + St());
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "droptest")   // test: the island's file zone: a file near, the drop, ZIP with progress, green and the check; on made-up files in a temp folder (with --cover)
                {
                    var dir = args.ElementAtOrDefault(r + 2) ?? System.IO.Path.GetTempPath();
                    Settings.ReadOnly = true;
                    Island.IsHitTestVisible = false; _settings.ExpandOnHover = false;
                    var work = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "atoll-droptest");
                    if (System.IO.Directory.Exists(work)) System.IO.Directory.Delete(work, true);
                    System.IO.Directory.CreateDirectory(work);
                    var rnd = new Random(7);
                    var files = new List<string>();
                    foreach (var n in new[] { "trip-photos.raw", "notes.txt", "mix.wav" })
                    {
                        var f = System.IO.Path.Combine(work, n);
                        var bytes = new byte[n == "notes.txt" ? 200_000 : 30_000_000];
                        rnd.NextBytes(bytes);
                        System.IO.File.WriteAllBytes(f, bytes);
                        files.Add(f);
                    }
                    await Task.Delay(3000);
                    int shot = 0;
                    void Shot(string what)
                    {
                        var v = DropView;
                        var rtb = new RenderTargetBitmap((int)Math.Ceiling(v.ActualWidth * 2) + 4, (int)Math.Ceiling(v.ActualHeight * 2) + 4, 192, 192, PixelFormats.Pbgra32);
                        var dv = new DrawingVisual();
                        using (var dc = dv.RenderOpen())
                        {
                            dc.DrawRectangle((Brush)FindResource("IslandBg"), null, new Rect(0, 0, v.ActualWidth + 2, v.ActualHeight + 2));
                            dc.DrawRectangle(new VisualBrush(v) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(1, 1, v.ActualWidth, v.ActualHeight));
                        }
                        rtb.Render(dv);
                        CaptureService.SavePng(rtb, System.IO.Path.Combine(dir, $"drop-{++shot}-{what}.png"));
                    }
                    string St() { var s = DropCoreView.TestState; return $"ring {s.Ring:0.00}, green {s.Green:0.00}, check {s.Check:0.00}, moving={s.Moving}, title '{DropTitle.Text}', line '{DropSubtitle.Text}'"; }
                    ShowDropTarget(); await Task.Delay(900); Shot("near"); Log.Write("droptest: a file near: " + St());
                    ShowDropped(files.ToArray()); await Task.Delay(700); Shot("dropped"); Log.Write("droptest: dropped: " + St());
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    _ = WorkOnDrop(files.ToArray(), L.T("Сжимаю в ZIP"), L.T("Архив готов"), p => FileDropActions.Zip(files, p));
                    await Task.Delay(250); Shot("working"); Log.Write("droptest: 0.25 s into ZIP: " + St());
                    var marks = new List<string>();
                    while (_dropWorking && sw.Elapsed.TotalSeconds < 30)
                    {
                        var s = DropCoreView.TestState;
                        if (s.Green > 0.6 && !marks.Contains("green")) { marks.Add("green"); Shot("done"); Log.Write($"droptest: green at {sw.Elapsed.TotalSeconds:0.0} s: " + St()); }
                        await Task.Delay(30);
                    }
                    var zip = System.IO.Directory.GetFiles(work, "*.zip").FirstOrDefault();
                    Log.Write($"droptest: work done at {sw.Elapsed.TotalSeconds:0.0} s, zip {(zip == null ? "MISSING" : $"{System.IO.Path.GetFileName(zip)} {new System.IO.FileInfo(zip).Length / 1e6:0.0} MB")}, mode={_mode}, island collapsed during the work: no");
                    await Task.Delay(500);
                    try { System.IO.Directory.Delete(work, true); } catch (Exception ex) { Log.Write("droptest: cleanup: " + ex.Message); }
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "gootest")   // test: the app row's goo menu: the fan of bubbles, picking Downloads, the toast; pictures (with --cover)
                {
                    var dir = args.ElementAtOrDefault(r + 2) ?? System.IO.Path.GetTempPath();
                    Settings.ReadOnly = true;
                    Island.IsHitTestVisible = false; _settings.ExpandOnHover = false;
                    _settings.DockApps.Clear();   // the pictures show none of the user's own apps (settings are read-only here)
                    await Task.Delay(3500);
                    _pinnedOpen = true; Expand(); await Task.Delay(600);
                    TabHome.IsChecked = true; BuildApps(); await Task.Delay(1500);
                    var g = _goo!;
                    void Shot(string what)
                    {
                        var rtb = new RenderTargetBitmap((int)Math.Ceiling(HomePage.ActualWidth * 1.5), (int)Math.Ceiling(HomePage.ActualHeight * 1.5), 144, 144, PixelFormats.Pbgra32);
                        var dv = new DrawingVisual();
                        using (var dc = dv.RenderOpen())
                        {
                            dc.DrawRectangle((Brush)FindResource("IslandBg"), null, new Rect(0, 0, HomePage.ActualWidth, HomePage.ActualHeight));
                            dc.DrawRectangle(new VisualBrush(HomePage) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top,
                                                                          ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, HomePage.ActualWidth, HomePage.ActualHeight) },
                                             null, new Rect(0, 0, HomePage.ActualWidth, HomePage.ActualHeight));
                        }
                        rtb.Render(dv);
                        CaptureService.SavePng(rtb, System.IO.Path.Combine(dir, $"goo-{what}.png"));
                    }
                    string St() { var s = g.TestState; return $"open={s.Open}, + turned {s.Turn:0}°, width {s.Width:0}, farthest bubble {s.FarthestBubble:0} px, moving={s.Moving}"; }
                    Log.Write("gootest: closed: " + St());
                    g.Toggle(); await Task.Delay(120); Shot("pouring"); Log.Write("gootest: 120 ms after opening: " + St());
                    await Task.Delay(900); Shot("open"); Log.Write("gootest: open: " + St());
                    g.TestPick(3); await Task.Delay(500); Shot("toast"); Log.Write("gootest: picked Downloads: " + St());
                    Log.Write($"gootest: Downloads in the row: {_settings.DockApps.Contains(Services.DownloadWatcher.DownloadsFolder(), StringComparer.OrdinalIgnoreCase)}");
                    await Task.Delay(3200); Log.Write("gootest: 3.7 s later: " + St());
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "wavetest")   // test: the song's name as a weight wave: the swell sweeping while it plays, resting on pause; pictures (with --cover)
                {
                    var dir = args.ElementAtOrDefault(r + 2) ?? System.IO.Path.GetTempPath();
                    Settings.ReadOnly = true;
                    Island.IsHitTestVisible = false; _settings.ExpandOnHover = false;
                    await Task.Delay(3500);
                    _pinnedOpen = true; Expand(); await Task.Delay(600);
                    TabMusic.IsChecked = true; await Task.Delay(1500);
                    var t = (Views.WeightWaveText)TrackTitle;
                    void Shot(string what)
                    {
                        double w = Math.Max(t.ActualWidth, 10) + 16, h = t.ActualHeight + 12;
                        var dv = new DrawingVisual();
                        using (var dc = dv.RenderOpen())
                        {
                            dc.DrawRectangle((Brush)FindResource("IslandBg"), null, new Rect(0, 0, w, h));
                            dc.DrawRectangle(new VisualBrush(t) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(8, 6, t.ActualWidth, t.ActualHeight));
                        }
                        var rtb = new RenderTargetBitmap((int)(w * 3), (int)(h * 3), 288, 288, PixelFormats.Pbgra32);
                        rtb.Render(dv);
                        CaptureService.SavePng(rtb, System.IO.Path.Combine(dir, $"wave-{what}.png"));
                    }
                    var trail = new List<string>();
                    for (int i = 0; i < 12; i++)
                    {
                        var st = t.TestState;
                        trail.Add($"{st.Boldest}");
                        if (i == 2 || i == 5 || i == 8) Shot($"playing-{i}");
                        await Task.Delay(400);
                    }
                    Log.Write($"wavetest: '{t.Text}', playing, boldest letter every 0.4 s: {string.Join(" ", trail)}, moving={t.TestState.Moving}");
                    _demoMedia = _demoMedia! with { Position = _demoMedia.CurrentPosition, PositionUpdatedAt = DateTimeOffset.Now, IsPlaying = false };
                    UpdateMediaUi(); await Task.Delay(2500);
                    var rest = t.TestState;
                    Log.Write($"wavetest: paused 2.5 s: strongest letter blend {rest.MaxBlend:0.00}, moving={rest.Moving}");
                    Shot("paused");
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "seektest")   // test: seeking with the jelly slider: the time in the drop, the jump on release (demo song only; with --cover)
                {
                    var dir = args.ElementAtOrDefault(r + 2) ?? System.IO.Path.GetTempPath();
                    Settings.ReadOnly = true;
                    Island.IsHitTestVisible = false; _settings.ExpandOnHover = false;
                    await Task.Delay(3500);
                    _pinnedOpen = true; Expand(); await Task.Delay(600);
                    TabMusic.IsChecked = true; await Task.Delay(1500);
                    var s = (Views.JellySlider)SeekSlider;
                    var m0 = _lastMedia!;
                    Log.Write($"seektest: playing at {FormatTime(m0.CurrentPosition)} of {FormatTime(m0.Duration)}, slider {s.Value:0.000} (song {m0.CurrentPosition.TotalSeconds / m0.Duration.TotalSeconds:0.000}), moving={s.TestState.Moving}");
                    await Task.Delay(1000);
                    Log.Write($"seektest: a second later: slider {s.Value:0.000}, moving={s.TestState.Moving} (playing alone must not keep frames going)");
                    double w = s.ActualWidth;
                    s.TestPress(10 + 0.7 * (w - 20)); await Task.Delay(400);
                    Log.Write($"seektest: held at 70%: drop says {s.Format!(s.Value)}, drop up {s.TestState.DropRise:0} px");
                    var rtb = new RenderTargetBitmap((int)Math.Ceiling(MusicPage.ActualWidth * 2), 220, 192, 192, PixelFormats.Pbgra32);
                    var dv = new DrawingVisual();
                    var row = (FrameworkElement)s.Parent;
                    var at = row.TranslatePoint(new Point(0, 0), MusicPage);
                    using (var dc = dv.RenderOpen())
                    {
                        dc.DrawRectangle((Brush)FindResource("IslandBg"), null, new Rect(0, 0, MusicPage.ActualWidth, 110));
                        dc.DrawRectangle(new VisualBrush(MusicPage) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top, ViewboxUnits = BrushMappingMode.Absolute,
                                                                      Viewbox = new Rect(0, at.Y - 70, MusicPage.ActualWidth, 110), ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, MusicPage.ActualWidth, 110) },
                                         null, new Rect(0, 0, MusicPage.ActualWidth, 110));
                    }
                    rtb.Render(dv);
                    CaptureService.SavePng(rtb, System.IO.Path.Combine(dir, "seek-held.png"));
                    s.TestRelease(); await Task.Delay(900);
                    var m1 = _lastMedia!;
                    Log.Write($"seektest: released: song now at {FormatTime(m1.CurrentPosition)} ({m1.CurrentPosition.TotalSeconds / m1.Duration.TotalSeconds:0.000}), slider {s.Value:0.000}, time on the left {Elapsed.Text}");
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "rubbertest")   // test: the rubber pill under the island's tabs and the settings menu: which edge leads, how far it stretches; pictures (with --cover)
                {
                    var dir = args.ElementAtOrDefault(r + 2) ?? System.IO.Path.GetTempPath();
                    Settings.ReadOnly = true;
                    Island.IsHitTestVisible = false; _settings.ExpandOnHover = false;
                    await Task.Delay(3500);
                    _pinnedOpen = true; Expand(); await Task.Delay(600);
                    TabHome.IsChecked = true; ShowTabIcons(true, animate: false); await Task.Delay(1200);
                    int shot = 0;
                    void Shot(FrameworkElement el, string what)
                    {
                        var rtb = new RenderTargetBitmap((int)Math.Ceiling(el.ActualWidth * 2) + 8, (int)Math.Ceiling(el.ActualHeight * 2) + 8, 192, 192, PixelFormats.Pbgra32);
                        var dv = new DrawingVisual();
                        using (var dc = dv.RenderOpen())
                        {
                            dc.DrawRectangle((Brush)FindResource("IslandBg"), null, new Rect(0, 0, el.ActualWidth + 4, el.ActualHeight + 4));
                            dc.DrawRectangle(new VisualBrush(el) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(2, 2, el.ActualWidth, el.ActualHeight));
                        }
                        rtb.Render(dv);
                        CaptureService.SavePng(rtb, System.IO.Path.Combine(dir, $"rubber-{++shot}-{what}.png"));
                    }
                    async Task Move(Views.RubberStack strip, RadioButton to, string name, FrameworkElement pic)
                    {
                        var s0 = strip.TestState; double rest = Math.Abs(s0.To - s0.From);
                        to.IsChecked = true;
                        await Task.Delay(30);
                        double maxLen = 0; var sw = System.Diagnostics.Stopwatch.StartNew();
                        var st = strip.TestState;
                        bool shotTaken = false;
                        while (sw.ElapsedMilliseconds < 1500)
                        {
                            st = strip.TestState;
                            maxLen = Math.Max(maxLen, Math.Abs(st.To - st.From));
                            if (!shotTaken && sw.ElapsedMilliseconds > 90) { Shot(pic, name); shotTaken = true; }
                            await Task.Delay(8);
                        }
                        st = strip.TestState;
                        Log.Write($"rubbertest: {name}: pill {rest:0} long at rest, stretched up to {maxLen:0} ({maxLen / Math.Max(1, rest):0.00}×), ends at {st.From:0}…{st.To:0}, still moving={st.Moving}");
                    }
                    // which edge leads: sample both edges 60 ms into a move to the right
                    var s1 = TabStrip.TestState;
                    TabWeather.IsChecked = true; await Task.Delay(60);
                    var s2 = TabStrip.TestState;
                    Log.Write($"rubbertest: 60 ms into a move right: leading (right) edge went {s2.To - s1.To:0.0} px, trailing (left) edge {s2.From - s1.From:0.0} px");
                    await Task.Delay(1500);
                    TabHome.IsChecked = true; await Task.Delay(1500);
                    await Move(TabStrip, TabSystem, "tabs-right", TabArea);
                    await Move(TabStrip, TabMusic, "tabs-left", TabArea);
                    TabSettings.IsChecked = true; await Task.Delay(1200);
                    if (SettingsNav.Children.Count > 3)
                    {
                        ((RadioButton)SettingsNav.Children[0]).IsChecked = true; await Task.Delay(1200);
                        await Move(SettingsNav, (RadioButton)SettingsNav.Children[3], "settings-down", SettingsNav);
                    }
                    ShowTabIcons(false, animate: false); await Task.Delay(300);
                    Shot(TabArea, "tabs-resting");
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "jellytest")   // test: the jelly slider in the Music tab: stretch, drop, lean, rubber, snap back; a settings slider's ticks (with --cover)
                {
                    var dir = args.ElementAtOrDefault(r + 2) ?? System.IO.Path.GetTempPath();
                    Settings.ReadOnly = true;
                    Island.IsHitTestVisible = false; _settings.ExpandOnHover = false;
                    await Task.Delay(3500);
                    _pinnedOpen = true; Expand(); await Task.Delay(600);
                    TabMusic.IsChecked = true; await Task.Delay(2000);
                    var s = (Views.JellySlider)VolumeSlider;
                    double w = s.ActualWidth;
                    int shot = 0;
                    void Shot(string what)
                    {
                        // the slider and the room above it (the drop rises out of its bounds), on the island's colour
                        double bw = w + 40, bh = 130;
                        var dv = new DrawingVisual();
                        using (var dc = dv.RenderOpen())
                        {
                            dc.DrawRectangle((Brush)FindResource("IslandBg"), null, new Rect(0, 0, bw, bh));
                            dc.PushTransform(new TranslateTransform(20, 60));
                            dc.DrawRectangle(new VisualBrush(s) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top,
                                                                  ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(-20, -60, bw, bh),
                                                                  ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(-20, -60, bw, bh) },
                                             null, new Rect(-20, -60, bw, bh));
                            dc.Pop();
                        }
                        var rtb = new RenderTargetBitmap((int)(bw * 2), (int)(bh * 2), 192, 192, PixelFormats.Pbgra32);
                        rtb.Render(dv);
                        CaptureService.SavePng(rtb, System.IO.Path.Combine(dir, $"jelly-{++shot}-{what}.png"));
                    }
                    string St() { var t = s.TestState; return $"x {t.X:0.0} (target {t.Target:0.0}), stretch {t.Stretch:0.00}, drop up {t.DropRise:0.0} px at {t.DropScale:0.00}, lean {t.Tilt:0.0}°, value {s.Value:0.00}, moving={t.Moving}"; }
                    Log.Write($"jellytest: width {w:0}, start: " + St());
                    s.TestPress(w * 0.3); await Task.Delay(350);
                    Log.Write("jellytest: pressed at 30%: " + St()); Shot("pressed");
                    double maxStretch = 1, maxLean = 0;
                    for (int i = 1; i <= 8; i++)
                    {
                        s.TestDrag(w * (0.3 + 0.6 * i / 8)); await Task.Delay(16);
                        var t = s.TestState; maxStretch = Math.Max(maxStretch, t.Stretch); maxLean = Math.Max(maxLean, Math.Abs(t.Tilt));
                        if (i == 5) Shot("dragging");
                    }
                    for (int i = 0; i < 10; i++) { await Task.Delay(16); var t = s.TestState; maxStretch = Math.Max(maxStretch, t.Stretch); maxLean = Math.Max(maxLean, Math.Abs(t.Tilt)); }
                    Log.Write($"jellytest: fast drag 30%→90%: most stretch {maxStretch:0.00}, most lean {maxLean:0.0}°, now " + St());
                    s.TestDrag(w + 80); await Task.Delay(400);
                    Log.Write($"jellytest: dragged 80 px past the end (thumb's end {w - 10:0}): " + St()); Shot("rubber");
                    s.TestRelease();
                    double past = 0; var sw = System.Diagnostics.Stopwatch.StartNew();
                    while (sw.ElapsedMilliseconds < 1500) { await Task.Delay(16); past = Math.Min(past, (w - 10) - s.TestState.X); }
                    Log.Write($"jellytest: released: back inside, the most past the end afterwards {-past:0.0} px, then " + St()); Shot("released");
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "morphtest")   // test: Morph island's motion: width lands before height, overshoot, volume, press; pictures mid-morph (with --cover)
                {
                    var dir = args.ElementAtOrDefault(r + 2) ?? System.IO.Path.GetTempPath();
                    Settings.ReadOnly = true;
                    Island.IsHitTestVisible = false; _settings.ExpandOnHover = false;
                    await Task.Delay(4000);
                    Log.Write($"morphtest: bouncy={_settings.Bouncy}, fast={_settings.FastAnimations}, reduced={Services.Motion.Reduced}");
                    async Task Record(string name, Action go, bool shots)
                    {
                        double w0 = _w.Value, h0 = _h.Value;
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        double wPeak = w0, hPeak = h0, wPeakAt = 0, hPeakAt = 0, maxGive = 0, minPress = 100, shownLow = double.MaxValue;
                        int frame = 0;
                        go();
                        double wT = _w.Target, hT = _h.Target;
                        bool growW = wT > w0, growH = hT > h0;
                        void OnFrame(object? s, EventArgs e)
                        {
                            double t = sw.Elapsed.TotalSeconds;
                            if (growW ? _w.Value > wPeak : _w.Value < wPeak) { wPeak = _w.Value; wPeakAt = t; }
                            if (growH ? _h.Value > hPeak : _h.Value < hPeak) { hPeak = _h.Value; hPeakAt = t; }
                            maxGive = Math.Max(maxGive, Math.Abs(Island.Width / Math.Max(1, _w.Value) - 1));
                            minPress = Math.Min(minPress, _press.Value);
                            shownLow = Math.Min(shownLow, Island.Height);
                            frame++;
                        }
                        CompositionTarget.Rendering += OnFrame;
                        foreach (var at in new[] { 0.08, 0.16, 0.26, 0.45, 0.9 })
                        {
                            while (sw.Elapsed.TotalSeconds < at) await Task.Delay(5);
                            if (!shots) continue;
                            var rtb = new RenderTargetBitmap((int)Math.Ceiling(Root.ActualWidth), (int)Math.Ceiling(Math.Min(Root.ActualHeight, 520)), 96, 96, PixelFormats.Pbgra32);
                            rtb.Render(Root);
                            CaptureService.SavePng(rtb, System.IO.Path.Combine(dir, $"morph-{name}-{at * 1000:000}ms.png"));
                        }
                        await Task.Delay(800);
                        CompositionTarget.Rendering -= OnFrame;
                        double Over(double peak, double from, double to) => Math.Abs(to - from) < 1 ? 0 : (peak - to) / (to - from) * 100;
                        Log.Write($"morphtest: {name}: width {w0:0}→{wT:0} peak {wPeak:0.0} at {wPeakAt * 1000:0} ms (over {Over(wPeak, w0, wT):0.0}%), " +
                                  $"height {h0:0}→{hT:0} peak {hPeak:0.0} at {hPeakAt * 1000:0} ms (over {Over(hPeak, h0, hT):0.0}%), " +
                                  $"shown height down to {shownLow:0.0}, volume up to {maxGive * 100:0.0}%, press down to {minPress / 100:0.000}, settled={_w.IsSettled && _h.IsSettled && _press.IsSettled}, {frame} frames");
                    }
                    ResetCosts();
                    await Record("open", () => { TabHome.IsChecked = true; _pinnedOpen = true; Expand(); }, shots: true);
                    await Record("close", () => { _pinnedOpen = false; SetMode(Mode.Compact); }, shots: true);
                    await Record("toast", () => ShowToast("\uE73E", "Atoll", L.T("Готово")), shots: false);
                    Log.Write("morphtest: " + TopCostsReport().Replace(Environment.NewLine, " | "));
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "musicrecord")   // test: the record and its bars in the Music tab and on the music widget, playing and paused (with --cover)
                {
                    var dir = args.ElementAtOrDefault(r + 2) ?? System.IO.Path.GetTempPath();
                    Settings.ReadOnly = true;
                    Island.IsHitTestVisible = false; _settings.ExpandOnHover = false;
                    await Task.Delay(3500);
                    _pinnedOpen = true; Expand(); await Task.Delay(600);
                    TabMusic.IsChecked = true; await Task.Delay(2500);
                    void Shot(FrameworkElement el, string name)
                    {
                        var rtb = new RenderTargetBitmap((int)Math.Ceiling(el.ActualWidth * 2), (int)Math.Ceiling(el.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
                        rtb.Render(el);
                        CaptureService.SavePng(rtb, System.IO.Path.Combine(dir, name + ".png"));
                    }
                    static T? FindChild<T>(DependencyObject d) where T : DependencyObject
                    {
                        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
                        {
                            var c = VisualTreeHelper.GetChild(d, i);
                            if (c is T t) return t;
                            if (FindChild<T>(c) is { } deeper) return deeper;
                        }
                        return null;
                    }
                    async Task<string> Motion(Views.MusicRecordView rec, Views.MusicBarsView bars)
                    {
                        double a0 = rec.Angle, l0 = bars.Level;
                        await Task.Delay(1000);
                        return $"record turned {(rec.Angle - a0 + 360) % 360:0.0}° in 1 s, bars {(Math.Abs(bars.Level - l0) > 0.01 ? "moving" : "still")}";
                    }
                    Log.Write("musicrecord: island, playing: " + await Motion(ArtRecord, MusicBars));
                    Shot(MusicPage, "island-music-playing");
                    _demoMedia = _demoMedia! with { Position = _demoMedia.CurrentPosition, PositionUpdatedAt = DateTimeOffset.Now, IsPlaying = false };
                    UpdateMediaUi(); await Task.Delay(800);
                    Log.Write("musicrecord: island, paused: " + await Motion(ArtRecord, MusicBars));
                    Shot(MusicPage, "island-music-paused");
                    _demoMedia = _demoMedia! with { PositionUpdatedAt = DateTimeOffset.Now, IsPlaying = true };
                    UpdateMediaUi();
                    _pinnedOpen = false; SetMode(Mode.Compact);
                    _settings.DesktopWidgets.Clear(); _settings.WidgetPlaces.Clear();
                    foreach (var letter in new[] { "M", "L" })
                    {
                        _settings.WidgetSizeOf["music"] = letter;
                        if (_widgets.ContainsKey("music")) RebuildWidget("music"); else AddWidget("music", pop: false);
                        _widgetTimer.Start();
                        await Task.Delay(2500);
                        var w = _widgets["music"];
                        var el = (FrameworkElement)w.Content;
                        var rec = FindChild<Views.MusicRecordView>(el); var bars = FindChild<Views.MusicBarsView>(el);
                        Log.Write($"musicrecord: widget {letter}: " + (rec == null || bars == null ? "NO RECORD" : await Motion(rec, bars)));
                        Shot(el, "widget-music-" + letter);
                    }
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "hidepaths")   // test: "hide in front of" by name, by program path and by folder (the taskbar belongs to explorer.exe)
                {
                    Settings.ReadOnly = true;
                    var dir = args.ElementAtOrDefault(r + 2) ?? System.IO.Path.GetTempPath();
                    var bar = Native.FindWindow("Shell_TrayWnd", null);
                    var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                    foreach (var entry in new[] { "explorer", System.IO.Path.Combine(win, "explorer.exe"), win, win + System.IO.Path.DirectorySeparatorChar, System.IO.Path.Combine(win, "System32"), System.IO.Path.Combine("D:", "Nothing", "Here"), "notepad" })
                    {
                        _settings.HideInApps = [entry];
                        _hiddenPid = 0;
                        Log.Write($"hidepaths: '{entry}' -> {(IsHiddenApp(bar) ? "hides" : "stays")}");
                    }
                    _settings.HideInApps = ["Zoom", System.IO.Path.Combine(win, "explorer.exe"), System.IO.Path.Combine("D:" + System.IO.Path.DirectorySeparatorChar, "Games", "Some Game")];
                    BuildHideAppChips();
                    var host = (FrameworkElement)VisualTreeHelper.GetParent(HideAppChips);
                    host.Visibility = Visibility.Visible;
                    host.Measure(new Size(560, double.PositiveInfinity)); host.Arrange(new Rect(0, 0, 560, host.DesiredSize.Height)); host.UpdateLayout();
                    var rtb = new RenderTargetBitmap(560 * 2, (int)Math.Ceiling(Math.Min(host.ActualHeight, 600) * 2), 192, 192, PixelFormats.Pbgra32);
                    rtb.Render(host);
                    CaptureService.SavePng(rtb, System.IO.Path.Combine(dir, "hidepaths.png"));
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "chatbuddy")   // test: the Claude Code buddy on the chat window covers none of the voice controls (with --cover)
                {
                    Settings.ReadOnly = true;
                    _settings.ExpandOnHover = false; _settings.Character = "clawd"; Character.Kind = "clawd";
                    _pinnedOpen = true; Expand();
                    await Task.Delay(600);
                    TabChat.IsChecked = true;   // after opening: the smart island may have picked another tab on the way
                    await Task.Delay(3000);
                    SetVoiceStatus("Слушаю… очень длинный статус голосового диалога");
                    await Task.Delay(500);
                    Rect Box(FrameworkElement e) => e.IsVisible ? e.TransformToAncestor(Root).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight)) : Rect.Empty;
                    var buddy = Box(ChatBuddy);
                    foreach (var (name, el) in new (string, FrameworkElement)[] { ("voice name", VoiceNameButton), ("voice", VoiceAssistantButton), ("voice status", VoiceStatusText), ("chat size", ChatSizeButton) })
                    {
                        var b = Box(el);
                        Log.Write($"chatbuddy: {name} {b.Left:0}..{b.Right:0} x {b.Top:0}..{b.Bottom:0}, buddy {buddy.Left:0}..{buddy.Right:0} x {buddy.Top:0}..{buddy.Bottom:0}: {(b.IsEmpty || buddy.IsEmpty ? "NOT SHOWN" : b.IntersectsWith(buddy) ? "COVERED" : "clear")}");
                    }
                    // no picture: the chat page shows the user's own signed-in chat, even in a demo run
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "crashtest")   // test: an error in a timer (250 times) and in a forgotten task: the island lives on, the log stays short
                {
                    Settings.ReadOnly = true;
                    int k = 0;
                    var bad = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
                    bad.Tick += (_, _) => Measure("crashtest", () => { if (++k > 250) { bad.Stop(); return; } throw new InvalidOperationException("crashtest tick"); });
                    bad.Start();
                    _ = Task.Run(() => throw new ApplicationException("crashtest task"));
                    await Task.Delay(5000);
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    await Task.Delay(500);
                    Log.Write($"crashtest: still running after {k - 1} errors, mode={_mode}, tick on={_tick.IsEnabled}");
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "karaokecost")   // test: what the karaoke line costs while the song plays and while it is paused (with --cover)
                {
                    Settings.ReadOnly = true;
                    Island.IsHitTestVisible = false; _settings.ExpandOnHover = false; _settings.Karaoke = true;
                    await Task.Delay(4000);
                    SetMode(Mode.Compact);
                    await DemoCommand("music");
                    foreach (var playing in new[] { true, false })
                    {
                        if (!playing) { _demoMedia = _demoMedia! with { Position = _demoMedia.CurrentPosition, PositionUpdatedAt = DateTimeOffset.Now, IsPlaying = false }; UpdateMediaUi(); }
                        await Task.Delay(3000);
                        ResetCosts();
                        await Task.Delay(20000);
                        Log.Write($"karaokecost: {(playing ? "playing" : "paused")}, line visible={Karaoke.IsVisible}: " + TopCostsReport().Replace(Environment.NewLine, " | "));
                    }
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "karaoketest")   // pictures and widths of the karaoke pill as lines change (with --cover)
                {
                    var dir = args.ElementAtOrDefault(r + 2) ?? System.IO.Path.GetTempPath();
                    Island.IsHitTestVisible = false; _settings.ExpandOnHover = false; _settings.Karaoke = true;
                    await Task.Delay(4000);
                    SetMode(Mode.Compact);
                    await DemoCommand("music");
                    void Shot(string name)
                    {
                        var rtb = new RenderTargetBitmap((int)Math.Ceiling(Root.ActualWidth * 2), 180, 192, 192, PixelFormats.Pbgra32);
                        rtb.Render(Root);
                        CaptureService.SavePng(rtb, System.IO.Path.Combine(dir, name + ".png"));
                    }
                    string seen = Karaoke.Text; int n = 0;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    while (n < 4 && sw.Elapsed.TotalSeconds < 40)
                    {
                        await Task.Delay(15);
                        if (Karaoke.Text == seen || !Karaoke.IsVisible) continue;
                        seen = Karaoke.Text; n++;
                        var widths = new List<string>();
                        var t0 = System.Diagnostics.Stopwatch.StartNew();
                        bool early = false, mid = false;
                        while (t0.ElapsedMilliseconds < 1700)
                        {
                            await Task.Delay(20);
                            long ms = t0.ElapsedMilliseconds;
                            if (ms % 100 < 22 && ms < 900) widths.Add($"{ms}:{Island.ActualWidth:0}");
                            if (!early && ms >= 90) { early = true; Shot($"line{n}-a-090ms"); }
                            if (!mid && ms >= 700) { mid = true; Shot($"line{n}-b-700ms"); }
                        }
                        Shot($"line{n}-c-1700ms");
                        Log.Write($"karaoketest: line {n} ({seen.Length} chars): target {_w.Target:0}, width over time {string.Join(" ", widths)}");
                    }
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "voicemenu")   // test: the voice menu opens whole (5 voices, nothing cut)
                {
                    Settings.ReadOnly = true; _settings.ExpandOnHover = false;
                    await Task.Delay(2500);
                    TabChat.IsChecked = true; _pinnedOpen = true; Expand();
                    await Task.Delay(1500);
                    VoiceName_Click(this, new RoutedEventArgs());
                    await Task.Delay(700);
                    var menu = VoiceNameButton.ContextMenu ?? System.Windows.Media.VisualTreeHelper.GetParent(VoiceNameButton) as object as System.Windows.Controls.ContextMenu;
                    foreach (var w in PresentationSource.CurrentSources.OfType<System.Windows.Interop.HwndSource>())
                        if (w.RootVisual is FrameworkElement fe && fe.GetType().Name.Contains("Popup"))
                        {
                            var items = 0; double h = 0;
                            void Walk(System.Windows.DependencyObject d) { for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++) { var c = System.Windows.Media.VisualTreeHelper.GetChild(d, i); if (c is System.Windows.Controls.MenuItem mi && mi.IsVisible) { items++; h += mi.ActualHeight; } Walk(c); } }
                            Walk(fe);
                            Log.Write($"voicemenu: pop-up {fe.ActualWidth:0}x{fe.ActualHeight:0}, visible items {items}, items height {h:0}");
                        }
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "geminitest")   // test: a Gemini Live session with the key from GEMINI_API_KEY (this process only); never logs the key
                {
                    Settings.ReadOnly = true;
                    var key = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";
                    string Clean(string t) => key.Length > 0 ? t.Replace(key, "<key>") : t;
                    var live = new GeminiLiveVoice(key, "Kore");
                    live.SetOutputVolume(0);   // silent: the test must not talk out loud
                    live.StatusChanged += st => Log.Write("geminitest: status " + st);
                    live.UserTranscribed += t => Log.Write("geminitest: heard " + t);
                    live.Failed += ex => Log.Write("geminitest: failed " + ex.GetType().Name + ": " + Clean(ex.Message));
                    try
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        await live.StartAsync();
                        Log.Write($"geminitest: setup complete in {sw.ElapsedMilliseconds} ms");
                        await live.SendTextAsync("Скажи одно короткое предложение: проверка связи.");
                        for (int i = 0; i < 30 && live.AudioBytesReceived == 0; i++) await Task.Delay(500);
                        await Task.Delay(3000);
                        Log.Write($"geminitest: audio received {live.AudioBytesReceived} bytes");
                    }
                    catch (Exception ex) { Log.Write("geminitest: start failed " + ex.GetType().Name + ": " + Clean(ex.Message) + (ex.InnerException != null ? " / " + Clean(ex.InnerException.Message) : "") + " -> shown: " + GeminiLiveVoice.Explain(ex, key)); }
                    await live.StopAsync();
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "keyfieldtest")   // test: a pasted key is kept at once and a settings refresh does not wipe it
                {
                    Settings.ReadOnly = true;
                    await Task.Delay(2000);
                    _settings.GeminiVoiceApiKey = "";
                    GeminiVoiceApiKeyBox.Password = "  \"AIzaTESTTESTTESTTEST\"  ";
                    bool saved = _settings.GeminiVoiceApiKey == "AIzaTESTTESTTESTTEST";
                    RefreshSettingsPage();
                    Log.Write($"keyfieldtest: kept at once={saved}, after refresh field kept={GeminiVoiceApiKeyBox.Password.Length > 0}, setting={_settings.GeminiVoiceApiKey.Length} chars");
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "mediaprobe")   // diagnostics: how often and how late the player reports its position (read only)
                {
                    var t0 = DateTime.Now;
                    while ((DateTime.Now - t0).TotalSeconds < 12)
                    {
                        var m = _media.Current;
                        Log.Write($"probe {(DateTime.Now - t0).TotalSeconds:0.00}: {_media.RawTimeline()} | extrapolated={m?.CurrentPosition.TotalSeconds:0.000} playing={m?.IsPlaying} | lyrics clock={LyricsPosition().TotalSeconds:0.000} resyncs={_lyricsClock.Resyncs}");
                        await Task.Delay(250);
                    }
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "glassshot")   // the glass on a test "wallpaper" shown by our own full-screen backdrop (the real wallpaper is never touched); tools/shot.ps1 takes the pictures
                {
                    var dir = args.ElementAtOrDefault(r + 2) ?? System.IO.Path.GetTempPath();
                    var wallName = args.ElementAtOrDefault(r + 3) ?? "light";
                    var mode = args.ElementAtOrDefault(r + 4) ?? "image";
                    Settings.ReadOnly = true;
                    System.IO.Directory.CreateDirectory(dir);
                    var screen = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
                    var toDip = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
                    var toPx = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
                    Log.Write($"glassshot: {wallName}, {mode}, screen {screen.Width}x{screen.Height}, scale {toPx.M11:0.00}");

                    // the test wallpaper: misty grey-white, or night
                    var wall = TestWallpaper(wallName == "dark", screen.Width, screen.Height);
                    CaptureService.SavePng(wall, System.IO.Path.Combine(dir, "wall-" + wallName + ".png"));
                    _wallpaper.Override = wall;
                    _wallpaper.BlurPx = _settings.GlassBlur;
                    _wallpaper.Rebuild();
                    bool gridMode = mode.StartsWith("grid"), musicMode = mode is "music" or "viz" or "lyrics" or "eq", weatherMode = mode == "weather", clockMode = mode == "clock", photosMode = mode == "photos", budgetMode = mode == "budget", notesMode = mode == "notes", systemMode = mode == "system";
                    if (musicMode || weatherMode || clockMode || photosMode || budgetMode || notesMode || systemMode) _forceGlass = Views.GlassKind.Image;
                    if (weatherMode) { _weatherFrozen = true; _lastWeather = DemoWeather(DateTime.Now, DateTime.Now); }
                    if (gridMode) { Spec = GridLayout.Get(mode[4..]); _forceGlass = Views.GlassKind.Image; }
                    if (mode == "live") _forceGlass = Views.GlassKind.Live;
                    if (mode == "solid") _forceGlass = Views.GlassKind.Solid;

                    var tl = toDip.Transform(new Point(screen.Left, screen.Top));
                    var br = toDip.Transform(new Point(screen.Right, screen.Bottom));
                    var backdrop = new Window
                    {
                        WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true, ShowActivated = false,
                        Left = tl.X, Top = tl.Y, Width = br.X - tl.X, Height = br.Y - tl.Y, Title = "Atoll glass test backdrop",
                        Content = new System.Windows.Controls.Image { Source = wall, Stretch = Stretch.UniformToFill },
                    };
                    backdrop.Show();
                    await Task.Delay(600);

                    _settings.DesktopWidgets.Clear(); _settings.WidgetPlaces.Clear(); _settings.WidgetThemeOf.Clear(); _settings.WidgetTheme = "liquid";
                    var (x0, y0, cols, rows) = WidgetGrid();
                    int c0 = Math.Max(0, cols / 2 - 3);
                    // the clock straddles the hills' edge (at 68 % of the height): the rim has something to bend
                    int r0 = Math.Clamp((int)Math.Round((toDip.Transform(new Point(0, screen.Top + screen.Height * 0.68)).Y - SmallSide * 0.5 - y0) / Step), 0, Math.Max(0, rows - 2));
                    // the clock and the list sit right on the hills' edge (not on the grid): the rim has an edge to bend
                    double hillY = toDip.Transform(new Point(0, screen.Top + screen.Height * 0.68)).Y - SmallSide * 0.5;
                    _settings.WidgetPlaces["clock"] = [x0 + c0 * Step, hillY];
                    _settings.WidgetPlaces["todo"] = [x0 + (c0 + 1) * Step, hillY];
                    _settings.WidgetPlaces["weather"] = [x0 + c0 * Step, hillY + Step];
                    var ids = new List<string> { "clock", "todo", "weather" };
                    if (gridMode)
                    {
                        // a home screen: two small, two medium, one large, on the grid of the variant
                        var g = Grids()[0].Grid;
                        int gc = Math.Max(0, g.Cols / 2 - 6), gr = 1;
                        Rect Cell(int c, int r) => g.Cell(gc + c, gr + r, 0, 0);
                        _settings.WidgetPlaces.Clear();
                        _settings.WidgetPlaces["clock"] = [Cell(0, 0).X, Cell(0, 0).Y];
                        _settings.WidgetPlaces["calendar"] = [Cell(0, 2).X, Cell(0, 2).Y];
                        _settings.WidgetPlaces["weather"] = [Cell(2, 0).X, Cell(2, 0).Y];
                        _settings.WidgetPlaces["todo"] = [Cell(2, 2).X, Cell(2, 2).Y];
                        _settings.WidgetPlaces["month"] = [Cell(6, 0).X, Cell(6, 0).Y];
                        _settings.WidgetSizeOf["month"] = "L";
                        ids = ["clock", "calendar", "weather", "todo", "month"];
                    }
                    // music: the same widget in its three sizes is shown as three separate windows of one id at a time,
                    // so it is added, shot and rebuilt per size below
                    if (musicMode) ids = ["music"];
                    if (weatherMode || clockMode || photosMode || budgetMode || notesMode || systemMode) ids = [];
                    foreach (var id in ids) AddWidget(id, pop: false);
                    _widgetTimer.Start();
                    foreach (var w in _widgets.Values) w.Front = true;   // above the test backdrop
                    await Task.Delay(1500);
                    Log.Write($"glassshot: widgets {string.Join(", ", _widgets.Select(p => p.Key + "=" + p.Value.Kind))}");


                    Rect Area()
                    {
                        var rs = _widgets.Values.Select(w => new Rect(w.Left, w.Top, w.Width, w.Height)).Aggregate(Rect.Union);
                        rs.Inflate(24, 24);
                        var a = toPx.Transform(rs.TopLeft); var b = toPx.Transform(rs.BottomRight);
                        return new Rect(a, b);
                    }
                    var wallPixels = new byte[wall.PixelWidth * wall.PixelHeight * 4];
                    wall.CopyPixels(wallPixels, wall.PixelWidth * 4, 0);
                    async Task Shot(string name, double dim = 0)
                    {
                        // our backdrop and the widgets on top again, in case anything came in front meanwhile
                        backdrop.Topmost = false; backdrop.Topmost = true;
                        foreach (var o in _gridOverlays) { o.Win.Topmost = false; o.Win.Topmost = true; }   // the edit-mode grid over the backdrop
                        foreach (var w in _widgets.Values) { w.Front = false; w.Front = true; }
                        await Task.Delay(300);
                        var a = Area();
                        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "ready-" + name + ".txt"), $"{(int)a.X} {(int)a.Y} {(int)a.Width} {(int)a.Height}");
                        var done = System.IO.Path.Combine(dir, name + ".done");
                        for (int i = 0; i < 150 && !System.IO.File.Exists(done); i++) await Task.Delay(200);
                        var png = System.IO.Path.Combine(dir, name + ".png");
                        if (!System.IO.File.Exists(done) || !System.IO.File.Exists(png)) { Log.Write($"glassshot: {name} NOT taken (is tools/shot.ps1 running?)"); return; }
                        // the picture must show our test wallpaper around the widgets; if anything else is there (the
                        // real desktop, another window), it is deleted at once: nothing of the user's screen is kept
                        bool ours = true;
                        try
                        {
                            var shot = new BitmapImage();
                            using (var fs = System.IO.File.OpenRead(png)) { shot.BeginInit(); shot.CacheOption = BitmapCacheOption.OnLoad; shot.StreamSource = fs; shot.EndInit(); }
                            var conv = new FormatConvertedBitmap(shot, PixelFormats.Bgra32, null, 0);
                            int sw2 = conv.PixelWidth, sh2 = conv.PixelHeight;
                            var px = new byte[sw2 * sh2 * 4];
                            conv.CopyPixels(px, sw2 * 4, 0);
                            foreach (var (cx, cy) in new[] { (2, 2), (sw2 - 3, 2), (2, sh2 - 3), (sw2 - 3, sh2 - 3) })
                            {
                                int wx = (int)a.X + cx - screen.Left, wy = (int)a.Y + cy - screen.Top;
                                if (wx < 0 || wy < 0 || wx >= wall.PixelWidth || wy >= wall.PixelHeight) { ours = false; break; }
                                int i1 = (cy * sw2 + cx) * 4, i2 = (wy * wall.PixelWidth + wx) * 4;
                                for (int ch = 0; ch < 3; ch++) if (Math.Abs(px[i1 + ch] - wallPixels[i2 + ch] * (1 - dim)) > 24) ours = false;
                            }
                        }
                        catch { ours = false; }
                        if (!ours) { System.IO.File.Delete(png); Log.Write($"glassshot: {name} did not show the test backdrop (is the desktop in front, after Win+D?): deleted"); }
                        else Log.Write($"glassshot: {name} taken");
                    }
                    if (musicMode && _widgets.TryGetValue("music", out _))
                    {
                        // a made-up cover (never a real one): violet night, then an orange sunset for the next track
                        BitmapSource Art(Color a, Color b)
                        {
                            var dv = new DrawingVisual();
                            using (var dc = dv.RenderOpen())
                            {
                                dc.DrawRectangle(new LinearGradientBrush(a, b, 45), null, new Rect(0, 0, 400, 400));
                                dc.DrawEllipse(new RadialGradientBrush(Color.FromArgb(0xC0, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)), null, new Point(260, 150), 120, 120);
                            }
                            var rtb = new RenderTargetBitmap(400, 400, 96, 96, PixelFormats.Pbgra32); rtb.Render(dv); rtb.Freeze();
                            return rtb;
                        }
                        BitmapImage ToImage(BitmapSource src)
                        {
                            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(src));
                            var ms = new System.IO.MemoryStream(); enc.Save(ms); ms.Position = 0;
                            var bi = new BitmapImage(); bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = ms; bi.EndInit(); bi.Freeze();
                            return bi;
                        }
                        var night = ToImage(Art(Color.FromRgb(0x3B, 0x1F, 0x8C), Color.FromRgb(0xE0, 0x4E, 0xA8)));
                        var sunset = ToImage(Art(Color.FromRgb(0xFF, 0x8A, 0x2B), Color.FromRgb(0xC2, 0x26, 0x3A)));
                        _lastMedia = new MediaSnapshot("Ночной город", "Demo Artist", "Spotify", "Spotify.exe", night, true, TimeSpan.FromSeconds(84), DateTimeOffset.Now, TimeSpan.FromSeconds(213));
                        if (mode == "eq")
                        {
                            // a stand-in APO folder in the test folder (the real one is never written by tests)
                            var fake = System.IO.Path.Combine(dir, "apo-config");
                            System.IO.Directory.CreateDirectory(fake);
                            System.IO.File.WriteAllText(System.IO.Path.Combine(fake, "config.txt"), "Preamp: -6 dB" + Environment.NewLine + "Include: example.txt" + Environment.NewLine);
                            System.IO.File.Delete(System.IO.Path.Combine(fake, EqualizerApo.FileName));
                            _eqDirOverride = fake;
                            _settings.EqCustom.Clear();
                            var rock = EqualizerApo.Presets.First(p => p.Name == "Рок");
                            _settings.EqGains = (double[])rock.Gains.Clone(); _settings.EqPreset = rock.Name; _settings.EqBypass = false;
                            _settings.MusicViz = "wave";
                            _settings.WidgetSizeOf["music"] = "L"; RebuildWidget("music");
                            var ew = _widgets["music"];
                            var eg = Grids()[0].Grid;
                            ew.CardX = eg.X0 + Math.Max(0, eg.Cols / 2 - 2) * Step; ew.CardY = eg.Y0 + Step;
                            ew.Front = true; UpdateWidgetGlass(ew);
                            TickWidgets();
                            await Task.Delay(1000);
                            await Shot(wallName + "-eq-closed");
                            ToggleEqPanel(_musicCard!);
                            await Task.Delay(900);
                            await Shot(wallName + "-eq-open");
                            _eqWriteNow?.Invoke();
                            Log.Write("eq test: WidgetEQ.txt = " + System.IO.File.ReadAllText(System.IO.Path.Combine(fake, EqualizerApo.FileName)).Replace(Environment.NewLine, " | "));
                            Log.Write("eq test: config.txt = " + System.IO.File.ReadAllText(System.IO.Path.Combine(fake, "config.txt")).Replace(Environment.NewLine, " | ") + "; backup " + System.IO.File.Exists(System.IO.Path.Combine(fake, "config.txt.atoll-backup")));
                            ToggleEqPanel(_musicCard!);
                            await Task.Delay(500);
                            _eqDirOverride = "";   // as if APO were not installed
                            ToggleEqPanel(_musicCard!);
                            await Task.Delay(900);
                            await Shot(wallName + "-eq-noapo");
                            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "finished.txt"), "ok");
                            foreach (var w in _widgets.Values.ToList()) w.Close();
                            backdrop.Close();
                            Application.Current.Shutdown();
                            return;
                        }
                        if (mode == "lyrics")
                        {
                            // made-up words (never a real song's), timed; the large widget alone
                            string[] words =
                            [
                                "Город спит, а мы опять не спим", "Свет фонарей рисует наши тени", "Этой ночью всё неважно, кроме нас",
                                "И музыка, что тихо в окна льётся", "Пусть утро подождёт ещё немного", "", "Мы говорим без слов, на языке огней",
                                "И каждый поворот ведёт нас к морю", "Не отпускай, пока горит рассвет", "Пусть утро подождёт ещё немного",
                            ];
                            _currentLyrics = new Lyrics(words.Select((w, i) => new LyricLine(TimeSpan.FromSeconds(60 + i * 7), w)).ToList(), true);
                            _lyricsKey = _lyricsLoaded = "Demo Artist|Ночной город";
                            _settings.MusicViz = "wave";
                            _settings.WidgetSizeOf["music"] = "L"; RebuildWidget("music");
                            var lw = _widgets["music"];
                            var lg = Grids()[0].Grid;
                            lw.CardX = lg.X0 + Math.Max(0, lg.Cols / 2 - 2) * Step; lw.CardY = lg.Y0 + Step;
                            lw.Front = true; UpdateWidgetGlass(lw);
                            _vizDemo = () => { var b = new float[AudioVisualizer.BandCount]; for (int i = 0; i < b.Length; i++) b[i] = (float)(0.55 * Math.Exp(-i / 14.0) + 0.12); return b; };
                            TickWidgets();
                            await Task.Delay(1500);
                            await Shot(wallName + "-lyrics-line");          // 84 s: the fourth line is being sung
                            _lastMedia = _lastMedia with { Position = TimeSpan.FromSeconds(104.5), PositionUpdatedAt = DateTimeOffset.Now };
                            await Task.Delay(1200);
                            await Shot(wallName + "-lyrics-scrolled");      // three lines on: the text has scrolled
                            // the sync shift: −0.5 s puts the line that has just started back to the one before
                            _lastMedia = _lastMedia with { Position = TimeSpan.FromSeconds(102.2), PositionUpdatedAt = DateTimeOffset.Now };
                            var before = LyricsService.LineAt(_currentLyrics.Lines, LyricsPosition() + TimeSpan.FromMilliseconds(200));
                            _settings.LyricsOffsetMs = -500;
                            var after = LyricsService.LineAt(_currentLyrics.Lines, LyricsPosition() + TimeSpan.FromMilliseconds(200));
                            _settings.LyricsOffsetMs = 0;
                            Log.Write($"glassshot: lyrics shift −0.5 s at 102.2 s: line {before} -> {after}");
                            // a song with no words found, and an instrumental
                            _currentLyrics = null;
                            TickWidgets();
                            await Task.Delay(800);
                            await Shot(wallName + "-lyrics-notfound");
                            _currentLyrics = new Lyrics([], false, Instrumental: true);
                            TickWidgets();
                            await Task.Delay(500);
                            await Shot(wallName + "-lyrics-instrumental");
                            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "finished.txt"), "ok");
                            foreach (var w in _widgets.Values.ToList()) w.Close();
                            backdrop.Close();
                            Application.Current.Shutdown();
                            return;
                        }
                        if (mode == "viz")
                        {
                            // made-up bands (a bass hump and moving ripples): pictures never depend on what plays
                            var vt0 = DateTime.Now;
                            _vizDemo = () =>
                            {
                                double t = (DateTime.Now - vt0).TotalSeconds;
                                var b = new float[AudioVisualizer.BandCount];
                                for (int i = 0; i < b.Length; i++)
                                {
                                    double x = (double)i / b.Length;
                                    b[i] = (float)Math.Clamp(0.78 * Math.Exp(-x * 2.4) * (0.75 + 0.25 * Math.Sin(t * 6 + i * 0.45)) + 0.28 * Math.Abs(Math.Sin(t * 2.7 + i * 0.33)) * (1 - x * 0.6), 0, 1);
                                }
                                return b;
                            };
                            var g2 = Grids()[0].Grid;
                            int gc2 = Math.Max(0, g2.Cols / 2 - 5);
                            var shown = new List<Views.DesktopWidgetWindow>();
                            void Place(string style)
                            {
                                foreach (var w in shown) w.Close();
                                shown.Clear(); _widgets.Clear(); _widgetUpdaters.Clear();
                                _settings.MusicViz = style;
                                foreach (var (letter, col) in new[] { ("M", 0), ("L", 4) })
                                {
                                    _settings.WidgetSizeOf["music"] = letter;
                                    _settings.WidgetPlaces["music"] = [g2.X0 + (gc2 + col) * Step, g2.Y0 + Step];
                                    ShowWidget("music");
                                    var w = _widgets["music"]; _widgets.Remove("music"); _widgets["music_" + letter] = w;
                                    _widgetUpdaters["music_" + letter] = _widgetUpdaters["music"]; _widgetUpdaters.Remove("music");
                                    shown.Add(w); w.Front = true; UpdateWidgetGlass(w);
                                }
                                TickWidgets();
                            }
                            _widgetTimer.Start();
                            foreach (var style in new[] { "wave", "bars", "ring" })
                            {
                                Place(style);
                                _vizTimer.Start();
                                await Task.Delay(1500);
                                await Shot(wallName + "-viz-" + style);
                            }
                            async Task<double> Measure(int ms)
                            {
                                using var proc = System.Diagnostics.Process.GetCurrentProcess();
                                var c0 = proc.TotalProcessorTime; var w0 = DateTime.Now;
                                await Task.Delay(ms);
                                proc.Refresh();
                                return (proc.TotalProcessorTime - c0).TotalMilliseconds / (DateTime.Now - w0).TotalMilliseconds / Environment.ProcessorCount * 100;
                            }
                            Place("wave");
                            _vizTimer.Start();
                            await Task.Delay(800);
                            double drawing = await Measure(10000);
                            // paused: everything stops
                            _vizDemo = null;
                            _lastMedia = _lastMedia with { IsPlaying = false };
                            VizControl(); _vizTimer.Stop();
                            await Task.Delay(1500);
                            double idle = await Measure(10000);
                            // the real capture, only if something is playing right now (nothing is played by the test itself)
                            string capture = "not measured (nothing was playing)";
                            if (_audio.Peak > 0.01f)
                            {
                                _lastMedia = _lastMedia with { IsPlaying = true };
                                VizControl();
                                await Task.Delay(800);
                                if (_viz.Running) capture = $"{await Measure(10000):0.00}%";
                                _viz.Stop(); _vizTimer.Stop();
                            }
                            Log.Write($"viz cpu ({Environment.ProcessorCount} threads): drawing 30 fps {drawing:0.00}%, paused {idle:0.00}%, with real capture {capture}");
                            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "finished.txt"), "ok");
                            foreach (var w in shown) w.Close();
                            backdrop.Close();
                            Application.Current.Shutdown();
                            return;
                        }
                        var g = Grids()[0].Grid;
                        int gc = Math.Max(0, g.Cols / 2 - 6);
                        double gx(int c) => g.X0 + (gc + c) * Step;
                        double gy = g.Y0 + Step;
                        // three sizes side by side: S, M, L
                        _settings.WidgetSizeOf["music"] = "S"; RebuildWidget("music");
                        var winS = _widgets["music"]; winS.CardX = gx(0); winS.CardY = gy;
                        _widgets.Remove("music"); _widgetUpdaters.Remove("music", out var updS);
                        _settings.WidgetSizeOf["music"] = "M"; ShowWidget("music");
                        var winM = _widgets["music"]; winM.CardX = gx(2); winM.CardY = gy;
                        _widgets.Remove("music"); _widgetUpdaters.Remove("music", out var updM);
                        _settings.WidgetSizeOf["music"] = "L"; ShowWidget("music");
                        var winL = _widgets["music"]; winL.CardX = gx(6); winL.CardY = gy;
                        _widgets["music_s"] = winS; _widgets["music_m"] = winM;
                        _widgetUpdaters["music_s"] = updS!; _widgetUpdaters["music_m"] = updM!;
                        foreach (var w in _widgets.Values) { w.Front = true; UpdateWidgetGlass(w); }
                        TickWidgets();
                        await Task.Delay(1200);
                        await Shot(wallName + "-music-rest");
                        // the mouse wheel over the medium one (an event inside the app; the real mouse is not touched)
                        if (winM.Host.Parent is FrameworkElement && winM.Host.Children[0] is FrameworkElement view)
                            for (int i = 0; i < 3; i++) view.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = MouseWheelEvent });
                        await Task.Delay(300);
                        await Shot(wallName + "-music-wheel");
                        // the next track: the glass takes the new cover's colour
                        _lastMedia = new MediaSnapshot("Закат над морем", "Demo Band", "Spotify", "Spotify.exe", sunset, true, TimeSpan.FromSeconds(12), DateTimeOffset.Now, TimeSpan.FromSeconds(187));
                        TickWidgets();
                        await Task.Delay(1500);
                        await Shot(wallName + "-music-next");
                        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "finished.txt"), "ok");
                        foreach (var w in _widgets.Values.ToList()) w.Close();
                        backdrop.Close();
                        Application.Current.Shutdown();
                        return;
                    }
                    if (systemMode)
                    {
                        // made-up load, temperature and programs; the real sensor is only read for the log
                        Log.Write($"system test: this PC's GPU sensor says {(GpuSensor.Temperature() is { } real ? $"{real:0}°" : "nothing (not supported)")}");
                        _statsDemo = new SystemStats.Snapshot(34, 62, 58, 18.6, 32, "Demo GPU");
                        var rnd = new Random(3);
                        for (int i = 0; i < SystemHistory.Samples; i++)
                        {
                            double k = i / (double)SystemHistory.Samples;
                            _sysHistory.Cpu.Add(Math.Clamp(18 + 14 * Math.Sin(k * 9) + rnd.Next(0, 12), 0, 100));
                            _sysHistory.Gpu.Add(Math.Clamp(40 + 25 * Math.Sin(k * 5 + 1) + rnd.Next(0, 10), 0, 100));
                            _sysHistory.Ram.Add(55 + 3 * k);
                            _sysHistory.NetDown.Add(Math.Max(0, 900_000 * Math.Sin(k * 14) + rnd.Next(0, 300_000)));
                        }
                        _gpuTemp = 64;
                        _cpuTopNow = [new("Browser", 9.4), new("Game", 7.1), new("Music", 2.3), new("Editor", 1.2), new("Chat", 0.6)];
                        var g = Grids()[0].Grid;
                        int gc = Math.Max(0, g.Cols / 2 - 6);
                        var shown = new List<Views.DesktopWidgetWindow>();
                        void Put(string key, string size, int col)
                        {
                            _settings.WidgetSizeOf["system"] = size;
                            _settings.WidgetPlaces["system"] = [g.X0 + (gc + col) * Step, g.Y0 + Step];
                            ShowWidget("system");
                            var w = _widgets["system"]; _widgets.Remove("system"); _widgets[key] = w;
                            _widgetUpdaters[key] = _widgetUpdaters["system"]; _widgetUpdaters.Remove("system");
                            shown.Add(w); w.Front = true; UpdateWidgetGlass(w);
                        }
                        foreach (var (size, col) in new[] { ("S", 0), ("M", 2), ("L", 6) }) Put("system_" + size, size, col);
                        TickWidgets();
                        await Task.Delay(1500);
                        await Shot(wallName + "-system-sizes");
                        // a real overheat: 93° for over half a minute (the clock is pretended forward, nothing waits)
                        _gpuTemp = 93;
                        _overheat.Update(93, DateTime.Now); _overheat.Update(93, DateTime.Now.AddSeconds(31));
                        for (int i = 0; i < 20; i++) _sysHistory.GpuTemp.Add(70 + i * 1.2);
                        TickWidgets();
                        await Task.Delay(800);
                        await Shot(wallName + "-system-hot");
                        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "finished.txt"), "ok");
                        foreach (var w in shown) w.Close();
                        backdrop.Close();
                        Application.Current.Shutdown();
                        return;
                    }
                    if (notesMode)
                    {
                        // made-up notes in this test process only (read-only: nothing is written over the user's notes)
                        NotesStore.Notes.Clear();
                        NotesStore.Notes.AddRange(
                        [
                            new Note { Text = "Позвонить маме в воскресенье\nи спросить про дачу", Created = DateTime.Now.AddHours(-2) },
                            new Note { Text = "Идеи для отпуска: Карпаты, море в сентябре", Created = DateTime.Now.AddDays(-1) },
                            new Note { Text = "Купить: хлеб, сыр, яблоки, кофе", Created = DateTime.Now.AddDays(-3) },
                            new Note { Text = "Пароль от Wi-Fi на холодильнике", Created = DateTime.Now.AddDays(-6) },
                        ]);
                        _settings.StickyNote = null;
                        var g = Grids()[0].Grid;
                        int gc = Math.Max(0, g.Cols / 2 - 6);
                        var shown = new List<Views.DesktopWidgetWindow>();
                        void Put(string key, string size, int col)
                        {
                            _settings.WidgetSizeOf["notes"] = size;
                            _settings.WidgetPlaces["notes"] = [g.X0 + (gc + col) * Step, g.Y0 + Step];
                            ShowWidget("notes");
                            var w = _widgets["notes"]; _widgets.Remove("notes"); _widgets[key] = w;
                            _widgetUpdaters[key] = _widgetUpdaters["notes"]; _widgetUpdaters.Remove("notes");
                            shown.Add(w); w.Front = true; UpdateWidgetGlass(w);
                        }
                        foreach (var (size, col) in new[] { ("S", 0), ("M", 2), ("L", 6) }) Put("notes_" + size, size, col);
                        _widgetTimer.Start();
                        TickWidgets();
                        await Task.Delay(1500);
                        await Shot(wallName + "-notes-sizes");
                        // no notes yet: the hint, not an empty card
                        foreach (var w in shown) w.Close();
                        shown.Clear(); foreach (var k in _widgets.Keys.Where(k => k.StartsWith("notes_")).ToList()) { _widgets.Remove(k); _widgetUpdaters.Remove(k); }
                        NotesStore.Notes.Clear();
                        Put("notes_S", "S", 0); Put("notes_M", "M", 2);
                        TickWidgets();
                        await Task.Delay(1200);
                        await Shot(wallName + "-notes-empty");
                        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "finished.txt"), "ok");
                        foreach (var w in shown) w.Close();
                        backdrop.Close();
                        Application.Current.Shutdown();
                        return;
                    }
                    if (budgetMode)
                    {
                        // a usual desktop: a clock, the weather (rain), photos, music (paused); made-up data only
                        _weatherFrozen = true;
                        _photoDemoFolder = System.IO.Path.Combine(dir, "demo-photos");
                        _photoDemoDates = MakeDemoPhotos(_photoDemoFolder);
                        _settings.PhotoMode = "folder";
                        var rainy = DemoWeather(DateTime.Now, DateTime.Now);
                        _lastWeather = rainy with { Code = 61 };
                        _lastMedia = new MediaSnapshot("Ночной город", "Demo Artist", "Spotify", "Spotify.exe", null, false, TimeSpan.FromSeconds(84), DateTimeOffset.Now, TimeSpan.FromSeconds(213));
                        var g = Grids()[0].Grid;
                        int gc = Math.Max(0, g.Cols / 2 - 6);
                        var shown = new List<Views.DesktopWidgetWindow>();
                        void Put(string id, string size, int col, int row)
                        {
                            _settings.WidgetSizeOf[id] = size;
                            _settings.WidgetPlaces[id] = [g.X0 + (gc + col) * Step, g.Y0 + (1 + row) * Step];
                            ShowWidget(id);
                            var w = _widgets[id]; shown.Add(w); w.Front = true; UpdateWidgetGlass(w);
                        }
                        void Layout()
                        {
                            foreach (var w in shown) w.Close();
                            shown.Clear(); _widgets.Clear(); _widgetUpdaters.Clear();
                            Put("clock", "S", 0, 0); Put("weather", "M", 2, 0); Put("photos", "S", 6, 0); Put("music", "M", 0, 2);
                            TickWidgets();
                        }
                        async Task<double> Measure(int ms)
                        {
                            using var proc = System.Diagnostics.Process.GetCurrentProcess();
                            var c0 = proc.TotalProcessorTime; var w0 = DateTime.Now;
                            await Task.Delay(ms);
                            proc.Refresh();
                            return (proc.TotalProcessorTime - c0).TotalMilliseconds / (DateTime.Now - w0).TotalMilliseconds / Environment.ProcessorCount * 100;
                        }
                        string Memory()
                        {
                            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                            using var proc = System.Diagnostics.Process.GetCurrentProcess();
                            return $"working set {proc.WorkingSet64 / 1048576} MB, private {proc.PrivateMemorySize64 / 1048576} MB, managed {GC.GetTotalMemory(false) / 1048576} MB";
                        }
                        await Task.Delay(15000);   // the start's own work (wallpaper caches, first layout) is over
                        double none = await Measure(15000);
                        string memNone = Memory();
                        _settings.ClockSweep = true; _settings.WeatherAtmosphere = true; _settings.PhotoKenBurns = true;
                        Layout();
                        _widgetTimer.Start();
                        await Task.Delay(3000);
                        await Shot(wallName + "-budget-rain");
                        double defaults = await Measure(15000);
                        string mem = Memory();
                        // snow, for the picture
                        _lastWeather = rainy with { Code = 73, Temp = -2 };
                        TickWidgets();
                        await Task.Delay(1500);
                        await Shot(wallName + "-budget-snow");
                        _lastWeather = rainy with { Code = 61 };
                        _settings.ClockSweep = false; _settings.WeatherAtmosphere = false;
                        Layout();
                        await Task.Delay(2000);
                        double calm = await Measure(15000);
                        _settings.WidgetsPaused = true;
                        UpdateWidgetPause();
                        await Task.Delay(1500);
                        double paused = await Measure(15000);
                        _settings.WidgetsPaused = false;
                        UpdateWidgetPause();
                        Log.Write($"budget ({Environment.ProcessorCount} threads): no widgets {none:0.00}%, 4 widgets with defaults (sweep, rain, Ken Burns) {defaults:0.00}%, without sweep and rain {calm:0.00}%, paused {paused:0.00}%; memory without widgets: {memNone}; with: {mem}");
                        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "finished.txt"), "ok");
                        foreach (var w in shown) w.Close();
                        backdrop.Close();
                        Application.Current.Shutdown();
                        return;
                    }
                    if (photosMode)
                    {
                        // made-up pictures in the test folder; the user's photos never appear
                        _photoDemoFolder = System.IO.Path.Combine(dir, "demo-photos");
                        _photoDemoDates = MakeDemoPhotos(_photoDemoFolder);
                        _settings.PhotoMode = "folder"; _settings.PhotoShuffle = false; _settings.PhotoSubfolders = false; _settings.PhotoFocus.Clear();
                        var g = Grids()[0].Grid;
                        int gc = Math.Max(0, g.Cols / 2 - 6);
                        var shown = new List<Views.DesktopWidgetWindow>();
                        Views.DesktopWidgetWindow Put(string key, string size, int col)
                        {
                            _settings.WidgetSizeOf["photos"] = size;
                            _settings.WidgetPlaces["photos"] = [g.X0 + (gc + col) * Step, g.Y0 + Step];
                            ShowWidget("photos");
                            var w = _widgets["photos"]; _widgets.Remove("photos"); _widgets[key] = w;
                            _widgetUpdaters[key] = _widgetUpdaters["photos"]; _widgetUpdaters.Remove("photos");
                            shown.Add(w); w.Front = true; UpdateWidgetGlass(w);
                            return w;
                        }
                        void CloseAll() { foreach (var w in shown) { w.Close(); } foreach (var k in _widgets.Keys.Where(k => k.StartsWith("photos_")).ToList()) { _widgets.Remove(k); _widgetUpdaters.Remove(k); } shown.Clear(); }
                        _photoListKey = "";
                        foreach (var (size, col) in new[] { ("S", 0), ("M", 2), ("L", 6) }) Put("photos_" + size, size, col);
                        _widgetTimer.Start();
                        for (int i = 0; i < 10 && _photoList == null; i++) { TickWidgets(); await Task.Delay(300); }
                        TickWidgets();
                        await Task.Delay(1800);
                        await Shot(wallName + "-photos-sizes");
                        // the focus point: the wide picture's subject is far right; in the middle it is cut off
                        CloseAll();
                        var wide = _photoList![0];
                        Put("photos_S1", "S", 0);   // without a focus point: the middle of the picture
                        TickWidgets();
                        await Task.Delay(1200);
                        _settings.PhotoFocus[wide] = [0.86, 0.42];
                        Put("photos_S2", "S", 2);   // with it: the subject
                        TickWidgets();
                        await Task.Delay(1500);
                        await Shot(wallName + "-photos-focus");
                        // this day: pictures taken on today's date in earlier years
                        CloseAll();
                        _settings.PhotoMode = "thisday"; _photoListKey = "";
                        Put("photos_L", "L", 2);
                        for (int i = 0; i < 10 && (_photoList == null || _photoListKey.Length == 0); i++) { TickWidgets(); await Task.Delay(300); }
                        TickWidgets();
                        await Task.Delay(1800);
                        await Shot(wallName + "-photos-thisday");
                        Log.Write($"photos test: this day = {string.Join(", ", (_photoList ?? []).Select(System.IO.Path.GetFileName))}, no memories = {_photoNoMemories}");
                        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "finished.txt"), "ok");
                        CloseAll();
                        backdrop.Close();
                        Application.Current.Shutdown();
                        return;
                    }
                    if (clockMode)
                    {
                        // made-up alarms and pomodoro count (read-only mode: nothing is saved)
                        _settings.Alarms = [new Alarm { Time = new TimeSpan(7, 30, 0), Days = AlarmService.Weekdays }, new Alarm { Time = new TimeSpan(10, 0, 0), Days = AlarmService.Weekend, Enabled = false }];
                        (_settings.PomodoroCountedOn, _settings.PomodoroDone) = (DateTime.Today, 3);
                        var g = Grids()[0].Grid;
                        int gc = Math.Max(0, g.Cols / 2 - 6);
                        var shown = new List<Views.DesktopWidgetWindow>();
                        Views.DesktopWidgetWindow Put(string key, string size, int col)
                        {
                            _settings.WidgetSizeOf["clock"] = size;
                            _settings.WidgetPlaces["clock"] = [g.X0 + (gc + col) * Step, g.Y0 + Step];
                            ShowWidget("clock");
                            var w = _widgets["clock"]; _widgets.Remove("clock"); _widgets[key] = w;
                            _widgetUpdaters[key] = _widgetUpdaters["clock"]; _widgetUpdaters.Remove("clock");
                            shown.Add(w); w.Front = true; UpdateWidgetGlass(w);
                            return w;
                        }
                        foreach (var (face, col) in new[] { ("classic", 0), ("minimal", 2), ("digital", 4) })
                        {
                            _settings.ClockFace = face;
                            Put("clock_" + face, "S", col);
                        }
                        _settings.ClockFace = "classic";
                        _widgetTimer.Start();
                        TickWidgets();
                        await Task.Delay(1500);
                        await Shot(wallName + "-clock-faces");
                        async Task<double> Measure(int ms)
                        {
                            using var proc = System.Diagnostics.Process.GetCurrentProcess();
                            var c0 = proc.TotalProcessorTime; var w0 = DateTime.Now;
                            await Task.Delay(ms);
                            proc.Refresh();
                            return (proc.TotalProcessorTime - c0).TotalMilliseconds / (DateTime.Now - w0).TotalMilliseconds / Environment.ProcessorCount * 100;
                        }
                        if (wallName == "dark")
                        {
                            double all = await Measure(10000);
                            // the same without the two sweeping faces: the difference is what the sweep costs
                            foreach (var key in new[] { "clock_classic", "clock_minimal" }) { var fw = _widgets[key]; fw.Close(); shown.Remove(fw); _widgets.Remove(key); _widgetUpdaters.Remove(key); }
                            await Task.Delay(1000);
                            double digitalOnly = await Measure(10000);
                            Log.Write($"clock cpu ({Environment.ProcessorCount} threads): two sweeping faces + digital {all:0.00}%, digital only {digitalOnly:0.00}%");
                        }
                        foreach (var w in shown) w.Close();
                        shown.Clear();
                        _timer.SetKind(TimerKind.Timer); _timer.SetDuration(TimeSpan.FromMinutes(10)); _timer.Start();
                        foreach (var tab in new[] { "clock", "timer", "pomodoro", "alarm" })
                        {
                            if (tab == "pomodoro") { _timer.SetKind(TimerKind.Pomodoro); _timer.Start(); }
                            _settings.ClockTab = tab;
                            var lw = Put("clock_L", "L", 2);
                            TickWidgets();
                            await Task.Delay(1200);
                            await Shot(wallName + "-clock-" + tab);
                            lw.Close(); shown.Remove(lw); _widgets.Remove("clock_L"); _widgetUpdaters.Remove("clock_L");
                        }
                        _timer.Reset();
                        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "finished.txt"), "ok");
                        backdrop.Close();
                        Application.Current.Shutdown();
                        return;
                    }
                    if (weatherMode)
                    {
                        // S, M and L side by side, then the same an hour without network (marked with its time)
                        var g = Grids()[0].Grid;
                        int gc = Math.Max(0, g.Cols / 2 - 6);
                        var shown = new List<Views.DesktopWidgetWindow>();
                        foreach (var (letter, col) in new[] { ("S", 0), ("M", 2), ("L", 6) })
                        {
                            _settings.WidgetSizeOf["weather"] = letter;
                            _settings.WidgetPlaces["weather"] = [g.X0 + (gc + col) * Step, g.Y0 + Step];
                            ShowWidget("weather");
                            var w = _widgets["weather"]; _widgets.Remove("weather"); _widgets["weather_" + letter] = w;
                            _widgetUpdaters["weather_" + letter] = _widgetUpdaters["weather"]; _widgetUpdaters.Remove("weather");
                            shown.Add(w); w.Front = true; UpdateWidgetGlass(w);
                        }
                        _widgetTimer.Start();
                        TickWidgets();
                        await Task.Delay(1500);
                        await Shot(wallName + "-weather-sizes");
                        Log.Write("weather test: advice = " + WeatherAdvice.Build(_lastWeather!, _lastWeather!.PlaceNow));
                        _lastWeather = DemoWeather(DateTime.Now, DateTime.Now.AddHours(-3));
                        TickWidgets();
                        await Task.Delay(800);
                        await Shot(wallName + "-weather-offline");
                        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "finished.txt"), "ok");
                        foreach (var w in shown) w.Close();
                        backdrop.Close();
                        Application.Current.Shutdown();
                        return;
                    }
                    if (gridMode)
                    {
                        await Shot(wallName + "-" + mode + "-layout");
                        EditWidgets(true);
                        await Task.Delay(900);
                        await Shot(wallName + "-" + mode + "-edit", 0x40 / 255.0);
                        // the small clock dropped onto the to-do list: the list moves aside
                        if (_widgets.TryGetValue("clock", out var ww) && _widgets.TryGetValue("todo", out var cw0))
                        {
                            Land(ww, new Rect(cw0.CardX, cw0.CardY, ww.CardW, ww.CardH));
                            await Task.Delay(1200);
                            await Shot(wallName + "-" + mode + "-push", 0x40 / 255.0);
                        }
                        // the clock's corner dragged out: the large size lights up on the grid, then it becomes large
                        if (_widgets.TryGetValue("clock", out var cw1))
                        {
                            ResizingWidget(cw1, new Point(cw1.CardX + 330, cw1.CardY + 330));
                            await Task.Delay(500);
                            await Shot(wallName + "-" + mode + "-resizing", 0x40 / 255.0);
                            ResizedWidget(cw1);
                            await Task.Delay(1500);
                            await Shot(wallName + "-" + mode + "-resized", 0x40 / 255.0);
                        }
                        System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "finished.txt"), "ok");
                        EditWidgets(false);
                        foreach (var w in _widgets.Values.ToList()) w.Close();
                        backdrop.Close();
                        Application.Current.Shutdown();
                        return;
                    }
                    await Shot(wallName + "-" + mode + "-rest");
                    // hover, without touching the real mouse: the clock is told the pointer is at its upper right
                    if (_widgets.TryGetValue("clock", out var cw))
                    {
                        cw.Card.Hover(true, new Point(cw.CardW * 0.85, cw.CardH * 0.15));
                        await Task.Delay(900);
                        await Shot(wallName + "-" + mode + "-hover");
                        cw.Card.Hover(false, null);
                    }
                    // the glass travels with the window: the whole group moves by a bit more than one cell
                    foreach (var w in _widgets.Values) { w.CardX += Step * 1.4; w.CardY -= Step * 0.6; }   // up, away from the taskbar
                    await Task.Delay(700);
                    await Shot(wallName + "-" + mode + "-moved");

                    System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "finished.txt"), "ok");
                    foreach (var w in _widgets.Values.ToList()) w.Close();
                    backdrop.Close();
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "widgettest")   // pictures of every desktop widget on a demo wallpaper, opened widgets, themes, the gallery, the island tab; where they sit in the z-order
                {
                    var dir = args.ElementAtOrDefault(r + 2) ?? System.IO.Path.GetTempPath();
                    Settings.ReadOnly = true;
                    await Task.Delay(3500);
                    _stats.Sample(); await Task.Delay(1100); _stats.Sample();
                    _settings.DesktopWidgets.Clear(); _settings.WidgetPlaces.Clear(); _settings.WidgetThemeOf.Clear(); _settings.WidgetTheme = "liquid";
                    Log.Write("widgettest: adding");
                    foreach (var k in WidgetKinds) { AddWidget(k.Id, pop: false); Log.Write("widgettest: added " + k.Id); }
                    _widgetTimer.Start();
                    await Task.Delay(2500);
                    // a made-up colourful wallpaper, so the pictures show what the glass does (the real glass is drawn by Windows and can't be rendered here)
                    var wall = new System.Windows.Controls.Grid { Width = 900, Height = 700 };
                    wall.Children.Add(new System.Windows.Shapes.Rectangle { Fill = new LinearGradientBrush(Color.FromRgb(0x1B, 0x26, 0x5C), Color.FromRgb(0x6A, 0x1B, 0x5C), 35) });
                    foreach (var (x, y, d, c) in new[] { (120.0, 90.0, 380.0, Color.FromRgb(0xFF, 0x7A, 0x45)), (560, 330, 420, Color.FromRgb(0x2E, 0xC4, 0xD8)), (250, 420, 300, Color.FromRgb(0xF2, 0x4B, 0x9B)), (640, 40, 240, Color.FromRgb(0xFF, 0xD2, 0x4A)) })
                    {
                        var e = new System.Windows.Shapes.Ellipse { Width = d, Height = d, Fill = new RadialGradientBrush(c, Color.FromArgb(0, c.R, c.G, c.B)), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(x - d / 2 + 100, y - d / 2 + 100, 0, 0) };
                        wall.Children.Add(e);
                    }
                    wall.Measure(new Size(900, 700)); wall.Arrange(new Rect(0, 0, 900, 700));
                    var wallBmp = new RenderTargetBitmap(900, 700, 96, 96, PixelFormats.Pbgra32); wallBmp.Render(wall); wallBmp.Freeze();
                    int spot = 0;
                    void GlassShot(Views.DesktopWidgetWindow w, string name)
                    {
                        var el = (FrameworkElement)w.Content;
                        double cw = el.ActualWidth, ch = el.ActualHeight, pad = 36;
                        var at = new Point(60 + (spot * 97) % Math.Max(1, 900 - cw - 120), 50 + (spot * 61) % Math.Max(1, 700 - ch - 100)); spot++;
                        var g = new System.Windows.Controls.Grid { Width = cw + pad * 2, Height = ch + pad * 2, ClipToBounds = true };
                        g.Children.Add(new System.Windows.Controls.Image { Source = wallBmp, Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-at.X + pad, -at.Y + pad, 0, 0) });
                        var glass = new System.Windows.Controls.Grid { Width = cw, Height = ch, Clip = new RectangleGeometry(new Rect(0, 0, cw, ch), Views.WidgetCard.Radius, Views.WidgetCard.Radius) };
                        glass.Children.Add(new System.Windows.Controls.Image { Source = wallBmp, Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-at.X, -at.Y, 0, 0), Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 30 } });
                        glass.Children.Add(new System.Windows.Shapes.Rectangle { Fill = new VisualBrush(el) { Stretch = Stretch.None } });
                        var holder = new System.Windows.Controls.Grid { Width = cw, Height = ch, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Opacity = 0.35 } };
                        holder.Children.Add(glass);
                        g.Children.Add(holder);
                        g.Measure(new Size(g.Width, g.Height)); g.Arrange(new Rect(0, 0, g.Width, g.Height)); g.UpdateLayout();
                        var rtb = new RenderTargetBitmap((int)(g.Width * 2), (int)(g.Height * 2), 192, 192, PixelFormats.Pbgra32);
                        rtb.Render(g);
                        CaptureService.SavePng(rtb, System.IO.Path.Combine(dir, name + ".png"));
                    }
                    void Live(Views.DesktopWidgetWindow w) => w.Card.Apply(WidgetThemeOf(w.Id), Views.GlassKind.Live);   // the see-through tint, as on a real desktop
                    foreach (var w in _widgets.Values) Live(w);
                    await Task.Delay(300);
                    Log.Write("widgettest: shooting");
                    foreach (var (id, w) in _widgets) GlassShot(w, "w-" + id);
                    Log.Write("widgettest: shot all");
                    // opened widgets
                    foreach (var id in new[] { "music", "weather", "month", "todo", "notes", "system", "timer", "worldclock", "stopwatch", "photos", "countdown", "launcher", "reminders", "clipboard", "water" })
                    {
                        if (!_widgets.TryGetValue(id, out var w)) continue;
                        ExpandWidget(id);
                        await Task.Delay(1400);
                        Live(w); await Task.Delay(100);
                        GlassShot(w, "open-" + id);
                        CollapseWidget(id);
                        await Task.Delay(900);
                    }
                    // themes
                    foreach (var t in Views.WidgetThemes.All)
                    {
                        SetWidgetTheme(null, t.Id);
                        foreach (var w in _widgets.Values) Live(w);
                        await Task.Delay(250);
                        if (_widgets.TryGetValue("todo", out var w1)) GlassShot(w1, "theme-" + t.Id + "-todo");
                        if (_widgets.TryGetValue("calendar", out var w2)) GlassShot(w2, "theme-" + t.Id + "-calendar");
                    }
                    SetWidgetTheme(null, "liquid");
                    // every widget in every size it has
                    foreach (var k in WidgetKinds)
                        foreach (var ch in SizesOf(k))
                        {
                            if (ch == Letter(k.Size)) continue;
                            _settings.WidgetSizeOf[k.Id] = ch.ToString(); RebuildWidget(k.Id);
                            await Task.Delay(750);
                            if (_widgets.TryGetValue(k.Id, out var sw)) { Live(sw); await Task.Delay(60); GlassShot(sw, $"size-{k.Id}-{ch}"); }
                        }
                    foreach (var id in _settings.WidgetSizeOf.Keys.ToList()) { _settings.WidgetSizeOf.Remove(id); RebuildWidget(id); }
                    // own colours: an accent, any colour for the glass, deeper glass
                    _settings.WidgetAccentOf["todo"] = "FF375F"; RebuildWidget("todo");
                    _settings.WidgetThemeOf["calendar"] = "custom:5E5CE6"; RebuildWidget("calendar");
                    _settings.WidgetThemeOf["clock"] = "custom:30D158"; _settings.WidgetGlassOf["clock"] = 1; RebuildWidget("clock");
                    await Task.Delay(800);
                    foreach (var id in new[] { "todo", "calendar", "clock" }) if (_widgets.TryGetValue(id, out var cw2)) { Live(cw2); cw2.ApplyTheme(WidgetThemeOf(id), GlassOf(id)); cw2.Card.Apply(WidgetThemeOf(id), Views.GlassKind.Live, GlassOf(id)); GlassShot(cw2, "custom-" + id); }
                    // the editor beside a widget
                    ShowWidgetEditor("weather");
                    await Task.Delay(900);
                    if (_widgetEditor?.Content is FrameworkElement ed)
                    {
                        var rtb = new RenderTargetBitmap((int)Math.Ceiling(ed.ActualWidth * 2), (int)Math.Ceiling(ed.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
                        rtb.Render(ed);
                        CaptureService.SavePng(rtb, System.IO.Path.Combine(dir, "editor.png"));
                    }
                    _widgetEditor?.Close();
                    // the right-click menu of a widget
                    if (_widgets.TryGetValue("todo", out var mw) && mw.Card.Card.ContextMenu is { } cm)
                    {
                        FillWidgetMenu(cm, "todo");
                        cm.PlacementTarget = mw.Card.Card; cm.IsOpen = true;
                        await Task.Delay(700);
                        var rtb = new RenderTargetBitmap((int)Math.Ceiling(cm.ActualWidth * 2), (int)Math.Ceiling(cm.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
                        rtb.Render(cm);
                        CaptureService.SavePng(rtb, System.IO.Path.Combine(dir, "menu.png"));
                        cm.IsOpen = false;
                    }
                    // where they sit: owned by the desktop and above it
                    var desktop = Native.FindWindow("Progman", null);
                    int owned = 0, above = 0;
                    foreach (var w in _widgets.Values)
                    {
                        if (GetWindow(w.Handle, 4) == desktop) owned++;
                        for (IntPtr h = GetWindow(w.Handle, 2); h != IntPtr.Zero; h = GetWindow(h, 2)) if (h == desktop) { above++; break; }
                    }
                    Log.Write($"widgettest: zorder: {owned}/{_widgets.Count} owned by the desktop, {above}/{_widgets.Count} above it");
                    // the gallery
                    EditWidgets(true, gallery: true);
                    await Task.Delay(1500);
                    if (_gridOverlays.Count > 0 && _gridOverlays[0].Win.Content is FrameworkElement gridEl)
                    {
                        var rtb = new RenderTargetBitmap((int)Math.Ceiling(gridEl.ActualWidth), (int)Math.Ceiling(gridEl.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                        rtb.Render(gridEl);
                        CaptureService.SavePng(rtb, System.IO.Path.Combine(dir, "grid.png"));
                    }
                    if (_widgetBar != null)
                    {
                        var el = (FrameworkElement)_widgetBar.Content;
                        var rtb = new RenderTargetBitmap((int)Math.Ceiling(el.ActualWidth * 2), (int)Math.Ceiling(el.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
                        rtb.Render(el);
                        CaptureService.SavePng(rtb, System.IO.Path.Combine(dir, "gallery.png"));
                        Log.Write($"widgettest: gallery at {_widgetBar.Left:0},{_widgetBar.Top:0} {_widgetBar.Width:0}x{_widgetBar.Height:0}");
                    }
                    EditWidgets(false);
                    await Task.Delay(800);
                    // the island tab
                    TabWidgets.IsChecked = true; _pinnedOpen = true; Expand();
                    await Task.Delay(1800);
                    {
                        var el = (FrameworkElement)Content;
                        var rtb = new RenderTargetBitmap((int)Math.Ceiling(el.ActualWidth * 2), (int)Math.Ceiling(el.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
                        rtb.Render(el);
                        CaptureService.SavePng(rtb, System.IO.Path.Combine(dir, "island-widgets.png"));
                    }
                    TabSystem.IsChecked = true;
                    await Task.Delay(1500);
                    {
                        var el = (FrameworkElement)Content;
                        var rtb = new RenderTargetBitmap((int)Math.Ceiling(el.ActualWidth * 2), (int)Math.Ceiling(el.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
                        rtb.Render(el);
                        CaptureService.SavePng(rtb, System.IO.Path.Combine(dir, "island-system.png"));
                    }
                    Log.Write($"widgettest: {_widgets.Count} widgets");
                    foreach (var w in _widgets.Values.ToList()) w.Close();
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "menutest")   // test: the right-click menu stays above the island while the island keeps itself on top
                {
                    Settings.ReadOnly = true; _settings.ExpandOnHover = false;
                    await Task.Delay(2500);
                    IslandMenu.PlacementTarget = Island;
                    IslandMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                    IslandMenu.IsOpen = true;
                    await Task.Delay(600);
                    for (int i = 0; i < 6; i++) { WatchFullscreen(); await Task.Delay(300); }
                    var menuHwnd = PresentationSource.FromVisual(IslandMenu) is System.Windows.Interop.HwndSource src ? src.Handle : IntPtr.Zero;
                    // walk the z-order from the top: which of the two comes first?
                    IntPtr h = GetTopWindow(IntPtr.Zero); string first = "neither";
                    for (int n = 0; h != IntPtr.Zero && n < 5000; n++, h = GetWindow(h, 2))
                    {
                        if (h == menuHwnd) { first = "menu"; break; }
                        if (h == _hwnd) { first = "island"; break; }
                    }
                    Log.Write($"menutest: menu open={IslandMenu.IsOpen}, menus counted={_menusOpen}, on top: {first}");
                    IslandMenu.IsOpen = false;
                    await Task.Delay(300);
                    Log.Write($"menutest: after closing, menus counted={_menusOpen}");
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "magtest")   // test: does the Magnification API give the screen without the island (which stays visible to capture)?
                {
                    Settings.ReadOnly = true;
                    var dir = args.ElementAtOrDefault(r + 2) ?? System.IO.Path.GetTempPath();
                    _settings.ExpandOnHover = false; _settings.Glass = false; _settings.CaptureHidden = false; ApplyCaptureExclusion(); ApplyGlass();
                    TabHome.IsChecked = true; _pinnedOpen = true; Expand();
                    await Task.Delay(2500);
                    var tl = Island.PointToScreen(new Point(0, 0));
                    var br = Island.PointToScreen(new Point(Island.ActualWidth, Island.ActualHeight));
                    var rect = new System.Drawing.Rectangle((int)tl.X, (int)tl.Y, (int)(br.X - tl.X), (int)(br.Y - tl.Y));
                    int w = rect.Width / 2, h = rect.Height / 2;
                    double Mean(byte[] px) { double sum = 0; for (int i = 0; i < px.Length; i += 4) sum += px[i] + px[i + 1] + px[i + 2]; return sum / (px.Length / 4 * 3); }
                    var plain = CaptureService.CaptureSmall(rect, w, h, null)!;
                    CaptureService.SavePng(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, plain, w * 4), System.IO.Path.Combine(dir, "mag-plain.png"));
                    Log.Write($"magtest: plain grab (island in it): mean {Mean(plain):0}");
                    for (int i = 0; i < 3; i++)
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        var mag = MagCapture.Capture(_hwnd, rect, w, h, null);
                        Log.Write($"magtest: mag grab #{i}: works={MagCapture.Works}, {(mag == null ? "null" : $"mean {Mean(mag):0}")}, {sw.Elapsed.TotalMilliseconds:0.0} ms");
                        if (mag != null && i == 0) CaptureService.SavePng(BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, mag, w * 4), System.IO.Path.Combine(dir, "mag-without.png"));
                        await Task.Delay(300);
                    }
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "glasscost")   // test: what the glass costs while the island stays visible to capture (as with NVIDIA's recorder)
                {
                    Settings.ReadOnly = true;
                    _settings.ExpandOnHover = false; _settings.Glass = true; _settings.CaptureHidden = false; ApplyCaptureExclusion(); ApplyGlass();
                    foreach (var open in new[] { false, true })
                    {
                        if (open) { TabHome.IsChecked = true; _pinnedOpen = true; Expand(); }
                        await Task.Delay(3000);
                        ResetCosts();
                        await Task.Delay(30000);
                        Log.Write($"glasscost: {(open ? "open" : "folded")}, mag works={MagCapture.Works}, last picture {MagCapture.LastSize.W}x{MagCapture.LastSize.H}: " + TopCostsReport().Replace(Environment.NewLine, " | "));
                    }
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "notiftest")   // test: does the notification listener see new toasts? (logs only)
                {
                    Settings.ReadOnly = true;
                    var nm = new NotificationMirror();
                    Log.Write($"notiftest: access={await nm.InitAsync()}");
                    await nm.DebugPollAsync();
                    for (int i = 0; i < 40; i++) { await Task.Delay(1000); await nm.DebugPollAsync(); }
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "cover")   // pictures of the newest features for the update cover (demo data, with --cover)
                {
                    var dir = args.ElementAtOrDefault(r + 2) ?? System.IO.Path.GetTempPath();
                    Island.IsHitTestVisible = false;
                    _settings.ExpandOnHover = false;
                    await Task.Delay(7000);
                    UpdateMediaUi(); SetMode(Mode.Compact); await Task.Delay(1500);
                    Snap(dir, "compact-music");
                    ShowToast("", L.T("Caps Lock включён"), null, null, new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)), ms: 6000);
                    await Task.Delay(1200); Snap(dir, "toast-caps");
                    await Task.Delay(5500);
                    ShowToast("", "EN", "English", null, null, ms: 6000);
                    await Task.Delay(1200); Snap(dir, "toast-layout");
                    await Task.Delay(5500);
                    _settings.HideInApps = ["Zoom", "Minecraft"];
                    _settings.LockPosition = true;
                    _settings.IslandScale = 100;
                    TabSettings.IsChecked = true; _pinnedOpen = true; Expand();
                    ((System.Windows.Controls.RadioButton)SettingsNav.Children[3]).IsChecked = true;
                    await Task.Delay(900);
                    RefreshSettingsPage();
                    BuildHideAppChips();
                    await Task.Delay(500);
                    HideAppChips.BringIntoView(new Rect(0, -60, 10, 330));
                    await Task.Delay(900);
                    Snap(dir, "settings-new");
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "features")   // test: island size, large chat, notes, hide-in-apps (nothing is saved)
                {
                    Settings.ReadOnly = true;
                    var dir = args.ElementAtOrDefault(r + 2) ?? System.IO.Path.GetTempPath();
                    Island.IsHitTestVisible = false;
                    _settings.ExpandOnHover = false;
                    await Task.Delay(5000);
                    foreach (var sc in new[] { 80, 100, 130 })
                    {
                        _settings.IslandScale = sc; ApplyIslandScale(); SetMode(Mode.Compact);
                        await Task.Delay(1200);
                        var b = Island.TransformToAncestor(Root).TransformBounds(new Rect(Island.RenderSize));
                        Log.Write($"features: scale {sc}: window {Width:0}x{Height:0}, island on screen {b.Width:0}x{b.Height:0} (layout {Island.ActualWidth:0}x{Island.ActualHeight:0})");
                        Snap(dir, $"scale-{sc}");
                    }
                    _settings.IslandScale = 100; ApplyIslandScale();
                    TabNotes.IsChecked = true; _pinnedOpen = true; Expand();
                    NewNoteBox.Text = string.Join(Environment.NewLine, Enumerable.Range(1, 40).Select(i => $"line {i}: " + new string('x', 60)));
                    await Task.Delay(1200);
                    Log.Write($"features: notes box keeps {NewNoteBox.LineCount} lines, {NewNoteBox.Text.Length} chars, box {NewNoteBox.ActualHeight:0}px");
                    Snap(dir, "notes-long");
                    NewNoteBox.Text = "";
                    foreach (var big in new[] { false, true, false })
                    {
                        _settings.ChatLarge = big; ApplyChatSize(); Remeasure();
                        await Task.Delay(400);
                        Log.Write($"features: chat large={big}: frame {ChatFrame.Height:0}, page {ChatPage.Width:0}, window {Height:0}, screen {SystemParameters.VirtualScreenHeight:0}");
                    }
                    var fg = Native.GetForegroundWindow();
                    Native.GetWindowThreadProcessId(fg, out uint fpid);
                    string fname = ""; try { fname = System.Diagnostics.Process.GetProcessById((int)fpid).ProcessName; } catch { /* exited meanwhile */ }
                    _settings.HideInApps = [];
                    bool before = IsHiddenApp(fg);
                    _settings.HideInApps = [fname];
                    _hiddenPid = 0;
                    Log.Write($"features: hide-in-apps fg='{fname}': empty list -> {before}, listed -> {IsHiddenApp(fg)}");
                    _pinnedOpen = false; SetMode(Mode.Compact);
                    ShowToast("", L.T("Caps Lock включён"), null, null, new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)), ms: 4000);
                    await Task.Delay(900);
                    Snap(dir, "toast-caps");
                    foreach (var hide in new[] { false, true })
                    {
                        _settings.CaptureHidden = hide; ApplyCaptureExclusion(); CaptureFrost(); UpdateGlassFill();
                        Native.GetWindowDisplayAffinity(_hwnd, out uint aff);
                        Log.Write($"features: hide from capture={hide}: affinity=0x{aff:X}, frost covers={_frostCovers}");
                    }
                    Log.Write($"features: vpn app = {ControlCenter.VpnApp()?.Name ?? "none"}");
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "smarttest")   // test: which tab the smart island picks, and messages waiting for a typing pause
                {
                    Settings.ReadOnly = true;
                    _settings.SmartIsland = true;
                    await Task.Delay(3000);
                    string Pick() => SmartTab()?.Name ?? "(keep current)";
                    _settings.TabHabits = new() { ["TabNotes"] = Enumerable.Repeat(4, 24).ToArray(), ["TabWeather"] = Enumerable.Repeat(1, 24).ToArray() };
                    _closedAt = DateTime.MinValue; _copiedAt = DateTime.MinValue;
                    Log.Write($"smarttest: habits (notes 4x weather) -> {Pick()}");
                    _copiedAt = DateTime.Now;
                    Log.Write($"smarttest: just copied -> {Pick()}");
                    _copiedAt = DateTime.MinValue;
                    _timer.Start();
                    Log.Write($"smarttest: timer running -> {Pick()}");
                    _timer.Reset();
                    _closedAt = DateTime.Now;
                    Log.Write($"smarttest: reopened right after closing -> {Pick()}");
                    _closedAt = DateTime.MinValue;
                    _settings.TabHabits = new();
                    Log.Write($"smarttest: no habits, nothing going on -> {Pick()}");
                    int shown = 0;
                    _typedAt = DateTime.Now;
                    WhenNotTyping(() => shown++);
                    Log.Write($"smarttest: while typing: shown={shown}, waiting={_deferred.Count}");
                    await Task.Delay(2600);
                    Log.Write($"smarttest: after the pause: shown={shown}, waiting={_deferred.Count}");
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "liquidtest")   // pictures of liquid glass over a made-up backdrop (stripes and a grid), never the real screen
                {
                    Settings.ReadOnly = true;
                    var dir = args.ElementAtOrDefault(r + 2) ?? System.IO.Path.GetTempPath();
                    Island.IsHitTestVisible = false; _settings.ExpandOnHover = false; NoClickOutside = true;
                    _frostTestBackdrop = (w, h) =>
                    {
                        var px = new byte[w * h * 4];
                        Color[] bars = [Color.FromRgb(0xFF, 0x5A, 0x5F), Color.FromRgb(0xFF, 0xB3, 0x40), Color.FromRgb(0x4C, 0xD9, 0x64), Color.FromRgb(0x2E, 0x9B, 0xFF), Color.FromRgb(0xAF, 0x52, 0xDE)];
                        for (int y = 0; y < h; y++)
                            for (int x = 0; x < w; x++)
                            {
                                var c = bars[(x / 22) % bars.Length];
                                bool grid = x % 22 < 2 || y % 14 < 2;
                                int i = (y * w + x) * 4;
                                px[i] = grid ? (byte)255 : c.B; px[i + 1] = grid ? (byte)255 : c.G; px[i + 2] = grid ? (byte)255 : c.R;
                            }
                        return px;
                    };
                    var preset = BuiltInThemes.First(t => t.Id == "liquid");
                    ApplyThemePreset(preset);
                    _settings.CaptureHidden = true; ApplyCaptureExclusion();
                    await Task.Delay(3000);
                    // the lens is a ps_3_0 shader, which RenderTargetBitmap (software) leaves out: the pictures are taken
                    // from the screen, the island visible to capture, over a window of test stripes ("cpu": Refract's look)
                    if (args.ElementAtOrDefault(r + 3) == "cpu") { _lensTried = true; _lens = null; ApplyLiquidBlur(); }
                    _settings.CaptureHidden = false; ApplyCaptureExclusion(); ApplyGlass();
                    var stripes = TestBackdrop(moving: false);
                    void Shot(string name)
                    {
                        double m = args.Contains("wide") ? 260 : 20;
                        var tl = Island.PointToScreen(new Point(-m, 0));
                        var br = Island.PointToScreen(new Point(Island.ActualWidth + m, Island.ActualHeight + m));
                        var rect = new System.Drawing.Rectangle((int)tl.X, (int)tl.Y, (int)(br.X - tl.X), (int)(br.Y - tl.Y));
                        var px = CaptureService.CaptureSmall(rect, rect.Width, rect.Height, null)!;
                        CaptureService.SavePng(BitmapSource.Create(rect.Width, rect.Height, 96, 96, PixelFormats.Bgr32, null, px, rect.Width * 4), System.IO.Path.Combine(dir, name + ".png"));
                    }
                    SetMode(Mode.Compact); await Task.Delay(2500); CaptureFrost(); await Task.Delay(300); Shot("liquid-compact");
                    TabMusic.IsChecked = true; _pinnedOpen = true; Expand(); await Task.Delay(2500); CaptureFrost(); await Task.Delay(300); Shot("liquid-open");
                    if (_liquidPixels != null && _frostBmp != null)
                        CaptureService.SavePng(BitmapSource.Create(_frostBmp.PixelWidth, _frostBmp.PixelHeight, 96, 96, PixelFormats.Bgr32, null, _liquidPixels, _frostBmp.PixelWidth * 4),
                                               System.IO.Path.Combine(dir, "liquid-bent-backdrop.png"));
                    Log.Write($"liquidtest: liquid={LiquidGlass} frost covers={_frostCovers} blur={(FrostBlur.Effect as System.Windows.Media.Effects.BlurEffect)?.Radius}, lens on the GPU={FrostLens.Effect != null}");
                    stripes.Close();
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "seasons")   // pictures of the four season themes, folded and open (with --cover)
                {
                    var dir = args.ElementAtOrDefault(r + 2) ?? System.IO.Path.GetTempPath();
                    Island.IsHitTestVisible = false; _settings.ExpandOnHover = false;
                    await Task.Delay(6000);
                    foreach (var id in Views.SeasonView.Kinds)
                    {
                        ApplyThemePreset(BuiltInThemes.First(t => t.Id == id));
                        _pinnedOpen = false; SetMode(Mode.Compact);
                        await Task.Delay(2500); Snap(dir, $"season-{id}-compact");
                        TabMusic.IsChecked = true; _pinnedOpen = true; Expand();
                        await Task.Delay(4200); Snap(dir, $"season-{id}-open");
                    }
                    // how much the scene costs: CPU time of this process over 10 s, open, with and without it
                    var proc = System.Diagnostics.Process.GetCurrentProcess();
                    foreach (var on in new[] { true, false })
                    {
                        if (!on) { _settings.Scene = ""; ApplyScene(); }
                        await Task.Delay(1000);
                        proc.Refresh(); var c0 = proc.TotalProcessorTime;
                        await Task.Delay(10000);
                        proc.Refresh();
                        Log.Write($"seasons: scene {(on ? "winter on" : "off")}: CPU {(proc.TotalProcessorTime - c0).TotalMilliseconds / 10000 / Environment.ProcessorCount * 100:0.0}% of the PC");
                    }
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "discordpill")   // pictures of the Discord call island (with --cover)
                {
                    var dir = args.ElementAtOrDefault(r + 2) ?? System.IO.Path.GetTempPath();
                    Island.IsHitTestVisible = false; _settings.ExpandOnHover = false;
                    _liveDemo = true;
                    await Task.Delay(6000);
                    void Shot(string name)
                    {
                        var rtb = new RenderTargetBitmap((int)Root.ActualWidth, (int)Root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                        rtb.Render(Root);
                        CaptureService.SavePng(rtb, System.IO.Path.Combine(dir, name + ".png"));
                    }
                    SetMode(Mode.Compact);
                    _demoDiscord = true;
                    await Task.Delay(1500); Shot("call-compact");
                    ShowKeyPill("\uE84A", L.T("Caps Lock включён"), null, Color.FromRgb(0xFF, 0x9F, 0x0A));
                    await Task.Delay(400); Shot("call-and-caps");
                    TabMusic.IsChecked = true; _pinnedOpen = true; Expand(); await Task.Delay(1500);
                    Shot("call-open");
                    _pinnedOpen = false; SetMode(Mode.Compact); _demoDiscord = false;
                    await Task.Delay(1200); Shot("call-ended");
                    Log.Write($"discordpill: lives = {string.Join(",", _liveIds)}");
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "dragtest")   // test: how far behind the cursor the dragged island is
                {
                    Settings.ReadOnly = true;
                    _settings.ExpandOnHover = false;
                    await Task.Delay(4000);
                    var home = new Point(Left, Top);
                    foreach (var spring in new[] { true, false })
                    {
                        DragOnSpring = spring;
                        var speed = new Vector(1200, 0);   // a brisk drag
                        var from = CursorDip();
                        _testCursor = (from, speed, DateTime.UtcNow);
                        _pressCursor = from; _pressWindow = new Point(Left, Top);
                        _pressed = true; _dragging = true; _posActive = true;
                        _px.Snap(Left); _py.Snap(Top);
                        _px.Stiffness = _py.Stiffness = 1500; _px.Damping = _py.Damping = 72;
                        StartRendering();
                        var lags = new List<double>();
                        EventHandler f = (_, _) =>
                        {
                            var (tx, ty) = DragTarget(CursorDip());
                            lags.Add(tx - _px.Value);
                            if (spring) { _px.Target = tx; _py.Target = ty; }   // what the mouse events did before
                        };
                        CompositionTarget.Rendering += f;
                        await Task.Delay(700);
                        CompositionTarget.Rendering -= f;
                        _testCursor = null; _pressed = false; _dragging = false;
                        var steady = lags.Skip(lags.Count / 3).ToList();
                        Log.Write($"dragtest: {(spring ? "spring (old)" : "direct (new)")}: behind the cursor by {steady.Average():0.0} px on average, at most {lags.Max():0.0} px ({steady.Average() / speed.X * 1000:0} ms at {speed.X} px/s)");
                        _px.Stiffness = _py.Stiffness = 560; _px.Damping = _py.Damping = 38;
                        MoveWindow(home.X, home.Y);
                        await Task.Delay(600);
                    }
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "musicplayer")   // test: musicplayer <source> <query> [folder]: plays the first result / opens the site, logs what Windows sees
                {
                    Settings.ReadOnly = true;
                    var id = args.ElementAtOrDefault(r + 2) ?? "radio";
                    _settings.MusicFolder = args.ElementAtOrDefault(r + 4) ?? "";
                    TabMusic.IsChecked = true; _pinnedOpen = true; Expand();
                    MusicQuery.Text = args.ElementAtOrDefault(r + 3) ?? "";
                    PickMusicSource(id); await Task.Delay(1500);
                    if (MusicQuery.Text.StartsWith("https://")) await OpenMusicSiteAsync(_musicSource, MusicQuery.Text); else await SearchMusicAsync();
                    Log.Write($"musicplayer: {id}: {MusicResults.Children.Count} results");
                    if (!args.Contains("picture") && MusicResults.Children.OfType<System.Windows.Controls.DockPanel>().FirstOrDefault()?.Children.OfType<System.Windows.Controls.Button>().LastOrDefault() is { } hit)
                        hit.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    if (args.Contains("picture"))   // only the Music page and its settings section, never a whole window
                    {
                        void Shot(FrameworkElement el, string name)
                        {
                            var bmp = new RenderTargetBitmap((int)el.ActualWidth + 1, (int)el.ActualHeight + 1, 96, 96, PixelFormats.Pbgra32);
                            var dv = new DrawingVisual();
                            using (var dc = dv.RenderOpen()) { var rc = new Rect(0, 0, el.ActualWidth, el.ActualHeight); dc.DrawRectangle(Brushes.Black, null, rc); dc.DrawRectangle(new VisualBrush(el), null, rc); }
                            bmp.Render(dv);
                            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp));
                            using var fs = System.IO.File.Create(System.IO.Path.Combine(System.IO.Path.GetTempPath(), name)); png.Save(fs);
                        }
                        await Task.Delay(9000); Shot(MusicPage, $"atoll-music-{id}.png");
                        Log.Write($"musicplayer: page {MusicPage.ActualWidth:0}x{MusicPage.ActualHeight:0}, island {Island.ActualWidth:0}x{Island.ActualHeight:0}, window {Height:0}");
                        Application.Current.Shutdown();
                        return;
                    }
                    long WebMb() => (_musicWeb?.CoreWebView2?.Environment.GetProcessInfos().Sum(i => { try { using var p = System.Diagnostics.Process.GetProcessById(i.ProcessId); return p.PrivateMemorySize64; } catch { return 0L; } }) ?? 0) / 1048576;
                    void Say(string what) => Log.Write($"musicplayer: {what}: shown [{_lastMedia?.SourceApp}|{_lastMedia?.AppId}|{_lastMedia?.Title}|{_lastMedia?.Artist}|art {_lastMedia?.Art != null}] playing {_lastMedia?.IsPlaying} pos {_lastMedia?.CurrentPosition.TotalSeconds:0.0} of {_lastMedia?.Duration.TotalSeconds:0.0}, own player {_player.Playing} at {_player.Position.TotalSeconds:0.0}, web audio {_musicWeb?.CoreWebView2?.IsDocumentPlayingAudio}, lyrics {_currentLyrics?.Lines.Count ?? 0}, {Environment.WorkingSet / 1048576} MB + web {WebMb()} MB");
                    await Task.Delay(_musicWeb != null ? 12000 : 9000); Say("started");
                    if (_musicWeb?.CoreWebView2 is { } site)
                    {
                        if (!site.IsDocumentPlayingAudio)   // a key press in the page, as the user would: space plays
                            foreach (var type in new[] { "keyDown", "keyUp" })
                                await site.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", $$"""{"type":"{{type}}","key":" ","code":"Space","windowsVirtualKeyCode":32,"text":" "}""");
                        await Task.Delay(7000); Say("space pressed");
                        _media.Choose("msedgewebview2.exe"); await Task.Delay(1500); Say("followed");
                        if (_media.Current?.AppId == "msedgewebview2.exe")
                        {
                            await _media.Seek(TimeSpan.FromSeconds(60)); await Task.Delay(2500); Say("seek to 60 s");
                            RefreshVolume(); Log.Write("musicplayer: volume " + VolumeLabel.Text);
                            await _media.TogglePlayPause(); await Task.Delay(1500); Say("paused");
                            await _media.TogglePlayPause(); await Task.Delay(1500); Say("resumed");
                            await _media.Next(); await Task.Delay(5000); Say("next");
                        }
                    }
                    _media.Choose("DynamicIsland.exe"); await Task.Delay(500);   // only Atoll's own player: the user's is left alone
                    if (_player.Open && Services.MediaService.IsOwn(_media.Current?.AppId ?? ""))
                    {
                        RefreshVolume(); Log.Write("musicplayer: volume " + VolumeLabel.Text);
                        await _media.TogglePlayPause(); await Task.Delay(1500); Say("paused");
                        await _media.TogglePlayPause(); await Task.Delay(1500); Say("resumed");
                        await _media.Next(); await Task.Delay(2500); Say("next");
                        await _media.Seek(TimeSpan.FromSeconds(20)); await Task.Delay(1500); Say("seek to 20 s");
                    }
                    if (_musicWeb != null) { _pinnedOpen = false; SetMode(Mode.Compact); await Task.Delay(6000); Say("folded"); }
                    _player.Pause(); await Task.Delay(1500);
                    _musicPlayedAt = DateTime.Now - TimeSpan.FromHours(1);
                    ReleaseIdleMusic(); await Task.Delay(3000); Say($"released (site {_musicWeb != null}, player {_player.Open})");
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "glassbench")   // test (2.1): what the glass costs and how often its picture changes: glassbench liquid|plain [mag]
                {
                    Settings.ReadOnly = true;
                    Island.IsHitTestVisible = false; _settings.ExpandOnHover = false; NoClickOutside = true;
                    bool liquid = args.ElementAtOrDefault(r + 2) != "plain";
                    bool mag = args.ElementAtOrDefault(r + 3) == "mag";
                    bool dragOnly = args.Contains("drag");   // only the drag phase (glassbench liquid drag)
                    ApplyThemePreset(BuiltInThemes.First(t => t.Id == (liquid ? "liquid" : "midnight")));
                    _settings.Glass = true; _settings.LiteGraphics = false;
                    _settings.CaptureHidden = !mag; ApplyCaptureExclusion(); ApplyGlass();
                    var me = System.Diagnostics.Process.GetCurrentProcess();
                    async Task Phase(string name, int secs)
                    {
                        await Task.Delay(1000);
                        ResetCosts(); _frostShownCount = 0; _frostGrabCount = 0; _frostGrabTicks = 0; _frostFades = 0; _frostFadeMs = 0;
                        me.Refresh(); var c0 = me.TotalProcessorTime; var t0 = DateTime.Now;
                        await Task.Delay(secs * 1000);
                        me.Refresh();
                        double wall = (DateTime.Now - t0).TotalMilliseconds, cpu = (me.TotalProcessorTime - c0).TotalMilliseconds;
                        Log.Write($"glassbench: {(liquid ? "liquid" : "plain")}{(mag ? " mag" : "")} {name}: CPU {cpu / wall * 100 / Environment.ProcessorCount:0.00}% of the PC ({cpu / wall * 100:0.0}% of one core), " +
                                  $"grabs {_frostGrabCount / (wall / 1000):0.0}/s at {(_frostGrabCount > 0 ? _frostGrabTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / _frostGrabCount : 0):0.00} ms off the UI thread, " +
                                  $"new pictures {_frostShownCount / (wall / 1000):0.0}/s ({_frostFades} faded in, {(_frostFades > 0 ? _frostFadeMs / _frostFades : 0):0} ms on average), mode {_mode}, covers={_frostCovers}, glass opacity {FrostHost.Opacity:0.00}");
                        Log.Write($"glassbench: {name}: " + TopCosts(_costs, (long)(wall / 1000 * System.Diagnostics.Stopwatch.Frequency), cpu / wall * 100 / Environment.ProcessorCount).Replace(Environment.NewLine, " | "));
                        string Frost(string n) => _costs.TryGetValue(n, out var c) ? $"{n} {c.Ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / (wall / 1000):0.00} ms/s, avg {c.Ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / c.Calls:0.00} ms, max {c.Max * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0.0} ms" : n + " -";
                        Log.Write($"glassbench: {name}: UI thread: {Frost("frost")}; {Frost("frost grab")}; {Frost("frost show")}");
                    }
                    if (args.Contains("game"))   // glassbench liquid mag game: moving frames under the pill, without and with a game in front
                    {
                        await Task.Delay(5000);
                        SetMode(Mode.Compact);
                        var frames = TestBackdrop(moving: true);
                        var under = new System.Windows.Interop.WindowInteropHelper(frames).Handle;
                        await Phase("no game", 10);
                        Log.Write($"glassbench: above the window under it: {Native.IsAbove(_hwnd, under)}, the other way round: {Native.IsAbove(under, _hwnd)}");
                        _demoGame = true; await Task.Delay(1500);
                        await Phase("game in front", 10);
                        _demoGame = false; await Task.Delay(1500);
                        await Phase("game left", 6);
                        Application.Current.Shutdown();
                        return;
                    }
                    await Task.Delay(dragOnly ? 5000 : 8000);
                    if (!dragOnly)
                    {
                    SetMode(Mode.Compact);
                    await Phase("folded", 10);
                    TabHome.IsChecked = true; _pinnedOpen = true; Expand();
                    await Phase("open", 10);
                    var gaps = new List<double>(); TimeSpan last = default;
                    EventHandler onFrame = (_, e2) => { var t = ((RenderingEventArgs)e2).RenderingTime; if (_rendering && last != default && t != last) gaps.Add((t - last).TotalMilliseconds); last = t; };
                    _pinnedOpen = false; SetMode(Mode.Compact); await Task.Delay(1500);
                    ResetCosts(); _frostShownCount = 0;
                    CompositionTarget.Rendering += onFrame;
                    for (int i = 0; i < 2; i++)
                    {
                        TabHome.IsChecked = true; _pinnedOpen = true; Expand(); await Task.Delay(900);
                        _pinnedOpen = false; SetMode(Mode.Compact); await Task.Delay(900);
                    }
                    CompositionTarget.Rendering -= onFrame;
                    var gg = gaps.Where(x => x < 500).ToList();
                    Log.Write($"glassbench: open/fold x2: {gg.Count} frames, {1000 / Math.Max(1, gg.DefaultIfEmpty(0).Average()):0} fps avg, worst {gg.DefaultIfEmpty(0).Max():0} ms, slow (>20 ms) {gg.Count(x => x > 20) * 100.0 / Math.Max(1, gg.Count):0}%, new pictures {_frostShownCount}");
                    Log.Write($"glassbench: open/fold: " + TopCosts(_costs, System.Diagnostics.Stopwatch.Frequency * 7, 0).Replace(Environment.NewLine, " | "));
                    Log.Write("glassbench: Smoothness: " + PerfSummary());
                    }
                    var backdrop = TestBackdrop(moving: true);
                    if (!dragOnly)
                    {
                    await Phase("folded over motion", 10);
                    TabHome.IsChecked = true; _pinnedOpen = true; Expand();
                    await Phase("open over motion", 10);
                    }
                    // dragging the folded pill over the stripes, back and forth (a made-up cursor): does the glass stay on?
                    _pinnedOpen = false; SetMode(Mode.Compact); await Task.Delay(1500);
                    foreach (var speed in new[] { 500.0, 1500.0 })
                    {
                        var home = new Point(Left, Top);
                        int frames = 0, covered = 0;
                        EventHandler count = (_, _) =>
                        {
                            frames++;
                            if (_frostCovers && FrostHost.Opacity > 0.5) covered++;
                        };
                        ResetCosts(); _frostShownCount = 0; _frostGrabCount = 0; _frostGrabTicks = 0;
                        me.Refresh(); var c0 = me.TotalProcessorTime; var t0 = DateTime.Now;
                        CompositionTarget.Rendering += count;
                        var from = new Point(Left + Width / 2, Top + 20);
                        _pressCursor = from; _pressWindow = home; _pressed = true; _dragging = true; _posActive = true;
                        _px.Snap(Left); _py.Snap(Top);
                        _px.Stiffness = _py.Stiffness = 1500; _px.Damping = _py.Damping = 72;
                        StartRendering();
                        foreach (var dir in new[] { 1, -1, 1, -1 })
                        {
                            _testCursor = (from, new Vector(dir * speed, dir * speed * 0.15), DateTime.UtcNow);
                            await Task.Delay((int)(300000 / speed));   // 300 DIP each way
                            from = CursorDip();
                        }
                        CompositionTarget.Rendering -= count;
                        me.Refresh();
                        double wall = (DateTime.Now - t0).TotalMilliseconds, cpu = (me.TotalProcessorTime - c0).TotalMilliseconds;
                        Log.Write($"glassbench: {(liquid ? "liquid" : "plain")} drag at {speed} DIP/s: CPU {cpu / wall * 100 / Environment.ProcessorCount:0.00}% of the PC, grabs {_frostGrabCount / (wall / 1000):0.0}/s, " +
                                  $"new pictures {_frostShownCount / (wall / 1000):0.0}/s, glass on in {covered * 100.0 / Math.Max(1, frames):0}% of {frames} frames");
                        _testCursor = null; _pressed = false; _dragging = false;
                        _px.Stiffness = _py.Stiffness = 560; _px.Damping = _py.Damping = 38;
                        MoveWindow(home.X, home.Y);
                        await Task.Delay(1500);
                    }
                    backdrop.Close();
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "liquidlook")   // test (2.1): liquid glass over dark / white / colourful / text backdrops, pictures and the contrast of its text: liquidlook <dir> [light] [theme:id]
                {
                    Settings.ReadOnly = true;
                    var dir = args.ElementAtOrDefault(r + 2) ?? System.IO.Path.GetTempPath();
                    System.IO.Directory.CreateDirectory(dir);
                    Island.IsHitTestVisible = false; _settings.ExpandOnHover = false; NoClickOutside = true;
                    ApplyThemePreset(BuiltInThemes.First(t => t.Id == (args.FirstOrDefault(a => a.StartsWith("theme:"))?[6..] ?? "liquid")));
                    if (args.Contains("light")) { _settings.DarkTheme = false; ApplyTheme(); ApplyScene(); ApplyThemeLocal(); }
                    _settings.LiteGraphics = false; _settings.CaptureHidden = false;
                    if (args.Contains("solid")) _settings.Glass = false;   // what Transparency effects off leaves
                    if (args.FirstOrDefault(a => a.StartsWith("tint:")) is { } lt) SceneTint = int.Parse(lt[5..]);
                    ApplyCaptureExclusion(); ApplyGlass();
                    async Task Look(string what)
                    {
                        var cover = LookBackdrop(what);
                        foreach (var page in new[] { "folded", "timer", "home", "settings" })
                        {
                            if (page == "folded") { _pinnedOpen = false; SetMode(Mode.Compact); }
                            else
                            {
                                if (page == "home") { HomeMissed.Clear(); for (int i = 0; i < 3; i++) HomeMissed.Add(new("", null, "Card " + i, "a message with some text in it", DateTime.Now, null)); }
                                (page == "timer" ? TabTimer : page == "home" ? TabHome : TabSettings).IsChecked = true;
                                _pinnedOpen = true; Expand();
                            }
                            await Task.Delay(1800);
                            Native.SetWindowPos(new System.Windows.Interop.WindowInteropHelper(cover).Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13);
                            Native.SetWindowPos(_hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x13);
                            await Task.Delay(900);
                            var tl = Island.PointToScreen(new Point(-16, 0));
                            var br = Island.PointToScreen(new Point(Island.ActualWidth + 16, Island.ActualHeight + 16));
                            var rect = new System.Drawing.Rectangle((int)tl.X, Math.Max(0, (int)tl.Y - 8), (int)(br.X - tl.X), (int)(br.Y - tl.Y) + 8);
                            // never the real screen: another program's window over the page, and the picture isn't taken
                            bool ours = true;
                            for (int gx = 0; gx <= 8; gx++)
                                for (int gy = 0; gy <= 8; gy++)
                                    ours &= Native.GetWindowThreadProcessId(WindowFromPoint(new Native.POINT { X = rect.Left + (rect.Width - 1) * gx / 8, Y = rect.Top + (rect.Height - 1) * gy / 8 }), out var pid) != 0 && pid == Environment.ProcessId;
                            if (!ours) { Log.Write($"liquidlook: {what} {page}: another window is over the test page, skipped"); continue; }
                            var px = CaptureService.CaptureSmall(rect, rect.Width, rect.Height, null)!;
                            CaptureService.SavePng(BitmapSource.Create(rect.Width, rect.Height, 96, 96, PixelFormats.Bgr32, null, px, rect.Width * 4), System.IO.Path.Combine(dir, $"{what}-{page}.png"));
                            Log.Write($"liquidlook: {what} {page}: {LookContrast(px, rect)}");
                        }
                        cover.Close();
                    }
                    await Task.Delay(3000);
                    foreach (var what in new[] { "dark", "white", "colour", "text" }) await Look(what);
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "perfreport")   // test: the smoothness report after a few openings
                {
                    Settings.ReadOnly = true;
                    await Task.Delay(4000);
                    for (int i = 0; i < 3; i++)
                    {
                        TabHome.IsChecked = true; _pinnedOpen = true; Expand(); await Task.Delay(900);
                        _pinnedOpen = false; SetMode(Mode.Compact); await Task.Delay(900);
                    }
                    Log.Write("perfreport: " + Environment.NewLine + PerfReport());
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "morphsize")   // test: does the size of the (layered) window decide how smooth it is?
                {
                    Settings.ReadOnly = true;
                    var gaps = new List<double>(); TimeSpan last = default;
                    EventHandler onFrame = (_, e2) => { var t = ((RenderingEventArgs)e2).RenderingTime; if (_rendering && last != default && t != last) gaps.Add((t - last).TotalMilliseconds); last = t; };
                    async Task Run(string name)
                    {
                        gaps.Clear();
                        CompositionTarget.Rendering += onFrame;
                        for (int i = 0; i < 4; i++)
                        {
                            TabHome.IsChecked = true; _pinnedOpen = true; Expand(); await Task.Delay(900);
                            _pinnedOpen = false; SetMode(Mode.Compact); await Task.Delay(900);
                        }
                        CompositionTarget.Rendering -= onFrame;
                        var g = gaps.Where(x => x < 500).ToList();
                        double avg = g.Count > 0 ? g.Average() : 0;
                        Log.Write($"morphsize: {name}: window {Width:0}x{Height:0} DIP x{Dpi:0.00}: {1000 / Math.Max(1, avg):0} fps avg, worst {(g.Count > 0 ? g.Max() : 0):0} ms, slow (>20 ms) {g.Count(x => x > 20) * 100.0 / Math.Max(1, g.Count):0}%");
                    }
                    await Task.Delay(5000);
                    PerfNoResize = true;
                    double cx = Left + Width / 2;
                    foreach (var (w, h) in new[] { (900.0, 720.0), (1800.0, 1440.0), (2700.0, 1440.0), (700.0, 520.0) })
                    {
                        Width = w; Height = h; MoveWindow(cx - w / 2, Top);
                        await Task.Delay(500);
                        await Run($"{w}x{h}");
                    }
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "morph")   // test: how smooth opening / closing is (frames per second while the island moves)
                {
                    Settings.ReadOnly = true;
                    var gaps = new List<double>(); TimeSpan last = default;
                    EventHandler onFrame = (_, e2) => { var t = ((RenderingEventArgs)e2).RenderingTime; if (_rendering && last != default && t != last) gaps.Add((t - last).TotalMilliseconds); last = t; };
                    async Task Run(string name)
                    {
                        gaps.Clear();
                        CompositionTarget.Rendering += onFrame;
                        for (int i = 0; i < 5; i++)
                        {
                            TabHome.IsChecked = true; _pinnedOpen = true; Expand(); await Task.Delay(900);
                            _pinnedOpen = false; SetMode(Mode.Compact); await Task.Delay(900);
                        }
                        CompositionTarget.Rendering -= onFrame;
                        var g = gaps.Where(x => x < 500).ToList();
                        double avg = g.Count > 0 ? g.Average() : 0;
                        Log.Write($"morph: {name}: {g.Count} frames, {1000 / Math.Max(1, avg):0} fps avg, worst {(g.Count > 0 ? g.Max() : 0):0} ms, slow (>20 ms) {g.Count(x => x > 20) * 100.0 / Math.Max(1, g.Count):0}%");
                    }
                    await Task.Delay(6000);
                    await Run("as is");
                    _settings.Glass = false; ApplyGlass(); FrostHost.Visibility = Visibility.Collapsed;
                    await Run("no frost");
                    _settings.Aura = "none"; _settings.ClaudeGlow = false; SetupAura();
                    await Run("no frost, no glow");
                    PerfNoResize = true; Height = FullWindowHeight;
                    await Run("window not resized");
                    PerfNoResize = false;
                    PerfNoChromatic = true;
                    await Run("no colour split");
                    PerfNoChromatic = false;
                    ShadowHost.Visibility = Visibility.Collapsed; ShadowHost.SetBinding(VisibilityProperty, new System.Windows.Data.Binding());
                    await Run("no shadow");
                    _settings.LiteGraphics = true; ApplyGlass();
                    await Run("lite");
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "perf")   // test: what the idle island costs, switching parts off one by one (nothing is saved)
                {
                    Settings.ReadOnly = true;
                    var me = System.Diagnostics.Process.GetCurrentProcess();
                    Log.Write($"perf: render tier {System.Windows.Media.RenderCapability.Tier >> 16}, window {Width}x{Height}");
                    async Task Phase(string name)
                    {
                        await Task.Delay(2000);
                        me.Refresh(); var c0 = me.TotalProcessorTime; var t0 = DateTime.Now;
                        await Task.Delay(10000);
                        me.Refresh();
                        Log.Write($"perf: {name}: {(me.TotalProcessorTime - c0).TotalMilliseconds / (DateTime.Now - t0).TotalMilliseconds * 100:0.0}% of one core");
                    }
                    await Task.Delay(8000);
                    await Phase("everything on");
                    _settings.Aura = "none"; _settings.ClaudeGlow = false; SetupAura();
                    await Phase("aura off");
                    Views.CharacterView.PerfFreeze = true;
                    await Phase("character still");
                    _settings.Glass = false; ApplyGlass(); FrostHost.Visibility = Visibility.Collapsed;
                    await Phase("+ no frosted glass");
                    ShadowHost.Visibility = Visibility.Collapsed; ShadowHost.SetBinding(VisibilityProperty, new System.Windows.Data.Binding());
                    await Phase("+ no shadow");
                    Island.Visibility = Visibility.Hidden;
                    await Phase("+ island hidden");
                    Application.Current.Shutdown();
                    return;
                }
                if (action == "fps-test")   // test: can we read frame events, and how many frames does the island itself show
                {
                    var ok = _fps.Start();
                    Kick(6);
                    await Task.Delay(6000);
                    var we = System.Diagnostics.Process.GetProcessesByName("wallpaper64").FirstOrDefault(); Log.Write($"fps-test: started={ok} denied={_fps.AccessDenied} own={_fps.FpsOf(Environment.ProcessId)} wallpaper={(we != null ? _fps.FpsOf(we.Id) : null)}");
                    _fps.Stop();
                    return;
                }
                if (action == "clean-test")   // test: clean desktop on for 4 s, then back
                {
                    bool was = CleanDesktop.TaskbarAutoHide, icons = CleanDesktop.IconsVisible;
                    CleanDesktop.Apply(true, was, icons);
                    await Task.Delay(4000);
                    Log.Write($"clean-test: on -> autohide={CleanDesktop.TaskbarAutoHide} icons={CleanDesktop.IconsVisible}");
                    CleanDesktop.Apply(false, was, icons);
                    await Task.Delay(1500);
                    Log.Write($"clean-test: off -> autohide={CleanDesktop.TaskbarAutoHide} icons={CleanDesktop.IconsVisible} (was {was}/{icons})");
                    return;
                }
                if (action == "update-dry")   // test: check + download + verify the newest installer, but don't run it
                {
                    try
                    {
                        var info = await Updater.CheckAsync();
                        if (info == null) { Log.Write("update-dry: up to date"); return; }
                        var path = await Updater.DownloadAsync(info, null);
                        Log.Write($"update-dry: {info.Tag} -> {path} ({new System.IO.FileInfo(path).Length} bytes, sha256 {(info.Sha256 != null ? "verified" : "not published")})");
                    }
                    catch (Exception ex) { Log.Write("update-dry failed: " + ex.Message); }
                    return;
                }
                if (action == "kick") { Kick(6); Squish(0.92); await Task.Delay(120); Unsquish(3); return; }
                if (action == "timer") { _timer.SetKind(TimerKind.Timer); _timer.SetDuration(TimeSpan.FromMinutes(secs > 0 ? secs : 5)); _timer.Start(); return; }
                _ = RunAction(action);
                if (action == "record" && secs > 0)
                {
                    await Task.Delay(secs * 1000);
                    _ = RunAction(action);
                }
            });
        }
#endif

#if DEBUG   // tests, pictures and demo videos: not in the release build
        if (snap >= 0)
            _ = RunSnapshotsAsync(snap + 1 < args.Length ? args[snap + 1] : System.IO.Path.Combine(AppContext.BaseDirectory, "snapshots"));
        if (review >= 0) _ = RunReviewAsync(args.ElementAtOrDefault(review + 1) ?? "review");
        int brand = Array.IndexOf(args, "--brand");
        if (brand >= 0) _ = RunBrandAsync(args.ElementAtOrDefault(brand + 1) ?? "brand");
        if (tiktok >= 0) _ = RunTikTokAsync(args.ElementAtOrDefault(tiktok + 1) ?? "tiktok", screen: false);
        if (tiktokScreen >= 0) _ = RunTikTokAsync(args.ElementAtOrDefault(tiktokScreen + 1) ?? "tiktok", screen: true);
        if (promo >= 0)
            _ = RunPromoAsync(args.ElementAtOrDefault(promo + 1) ?? "promo", args.ElementAtOrDefault(promo + 2) ?? "ru");
#endif

        await SafelyAsync("media", InitMediaAsync);
        await SafelyAsync("weather", InitWeatherAsync);
    }

    private void OnSourceInitialized()
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        DisablePowerThrottling();
        InitStallWatch();
        Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE) | Native.WS_EX_TOOLWINDOW);

        HwndSource.FromHwnd(_hwnd)!.AddHook(WndProc);
        WatchWidgetPause();
        // the frosted glass photographs the screen behind the island: without this the island would
        // photograph itself (the glass turned opaque a second after opening). Also keeps it out of screenshots.
        ApplyCaptureExclusion();
        Native.AddClipboardFormatListener(_hwnd);
        uint mods = Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT;
        foreach (var (id, vk, name) in new[] { (1, 0x49u, "I"), (2, 0x54u, "T"), (3, 0x53u, "S"), (4, 0x52u, "R") })
            if (!Native.RegisterHotKey(_hwnd, id, mods, vk))
                Log.Write($"Ctrl+Alt+{name} is taken by another program");
        RegisterCommandHotkey();
        RegisterOpenHotkey();
    }

    // ---------------------------------------------------------------- the command bar's shortcut

    public static readonly (string Name, uint Mods, uint Vk)[] CommandHotkeys =
    [
        ("Alt+Space", Native.MOD_ALT, 0x20),
        ("Ctrl+Alt+Space", Native.MOD_CONTROL | Native.MOD_ALT, 0x20),
        ("Ctrl+Shift+Space", Native.MOD_CONTROL | 0x4, 0x20),
    ];

    /// <summary>
    /// Registers the command bar's shortcut; false when another program already owns it. Alt+Space (the default)
    /// also takes Ctrl+Alt+Space, as before 2.0.1, in case another launcher already owns Alt+Space.
    /// </summary>
    private bool _commandKeyBackupOnly;

    private bool RegisterCommandHotkey()
    {
        if (_hwnd == IntPtr.Zero) return true;
        Native.UnregisterHotKey(_hwnd, 5);
        Native.UnregisterHotKey(_hwnd, 6);
        var hk = CommandHotkeys.FirstOrDefault(h => h.Name == _settings.CommandHotkey);
        if (hk.Name == null) return true;   // off
        bool ok = Native.RegisterHotKey(_hwnd, 5, hk.Mods | Native.MOD_NOREPEAT, hk.Vk);
        if (!ok) Log.Write($"{hk.Name} is taken by another program");
        bool backup = hk.Name == "Alt+Space" && Native.RegisterHotKey(_hwnd, 6, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, 0x20);
        _commandKeyBackupOnly = !ok && backup;   // the settings say so instead of a general hint
        return ok || backup;
    }


    // ---------------------------------------------------------------- open / fold the island from the keyboard

    public static readonly (string Name, uint Mods, uint Vk)[] OpenHotkeys =
    [
        ("Ctrl+Alt+O", Native.MOD_CONTROL | Native.MOD_ALT, 0x4F),
        ("Ctrl+Alt+A", Native.MOD_CONTROL | Native.MOD_ALT, 0x41),
        ("Alt+Q", Native.MOD_ALT, 0x51),
        ("Ctrl+Shift+Space", Native.MOD_CONTROL | 0x4, 0x20),
    ];

    /// <summary>Registers the chosen shortcut; false when another program already owns it.</summary>
    private bool RegisterOpenHotkey()
    {
        if (_hwnd == IntPtr.Zero) return true;
        Native.UnregisterHotKey(_hwnd, 7);
        var hk = OpenHotkeys.FirstOrDefault(h => h.Name == _settings.OpenHotkey);
        if (hk.Name == null) return true;   // off
        bool ok = Native.RegisterHotKey(_hwnd, 7, hk.Mods | Native.MOD_NOREPEAT, hk.Vk);
        if (!ok) Log.Write($"{hk.Name} is taken by another program");
        return ok;
    }

    private void ToggleOpenFromKeyboard()
    {
        if (_mode == Mode.Expanded)
        {
            _pinnedOpen = false;
            SetMode(Mode.Compact);
            return;
        }
        if (_hidden) ShowIsland();
        _pinnedOpen = true;   // stays open until the shortcut, Esc or a click elsewhere
        GrowWindow();
        Expand();
        Activate();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WidgetPause.WM_POWERBROADCAST && wParam.ToInt32() == WidgetPause.PBT_POWERSETTINGCHANGE) { OnPowerSetting(lParam); return IntPtr.Zero; }
        if (msg == Native.WM_CLIPBOARDUPDATE)
        {
            Dispatcher.BeginInvoke(OnClipboardChanged, DispatcherPriority.Background);
            return IntPtr.Zero;
        }
        if (_shellHookMsg != 0 && msg == _shellHookMsg)
        {
            if (wParam.ToInt32() == 0x8006) OnWindowFlash(lParam);   // HSHELL_FLASH: a taskbar button started to blink
            return IntPtr.Zero;
        }
        if (msg == Native.WM_HOTKEY)
        {
            switch (wParam.ToInt32())
            {
                case 1: ToggleHidden(); break;
                case 2: ToggleTheme(); break;
                case 3: _ = RunAction("region"); break;
                case 4: _ = RunAction("record"); break;
                case 5 or 6: ToggleCommandBar(); break;
                case 7: ToggleOpenFromKeyboard(); break;
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
        for (int id = 1; id <= 7; id++) Native.UnregisterHotKey(_hwnd, id);
        Native.RemoveClipboardFormatListener(_hwnd);
        _sideToast?.Close();
        ShutdownChat();
        ShutdownMusicPlayer();
        _settings.Save();
        MagCapture.MarkClean();   // a normal exit: Windows did not take us down inside the magnifier
        StartupGuard.Done();
        FocusMode.End(); // never leave Windows notifications switched off
        _fps.Dispose();
        ShutdownControlCenter(); // taskbar and desktop icons come back
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
        ShadowHost.Margin = Island.Margin;
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
        // moved while a program with its own place was in front (or floated to that place): it is that program's place
        if (_placeOwner != null) _settings.AppPlaces[_placeOwner] = [Left + Width / 2, Top];
        else
        {
            _settings.CenterX = Left + Width / 2;
            _settings.Top = Top;
        }
        _settings.Save();
    }

    private void ResetPosition_Click(object sender, RoutedEventArgs e) => AlignIsland();

    /// <summary>Float the island back to the exact top centre of the screen it is on.</summary>
    private void AlignIsland()
    {
        if (_hidden) ShowIsland();
        if (_settings.PlacePerApp && ForgetAppPlace()) return;
        var screen = Forms.Screen.FromPoint(new System.Drawing.Point((int)(Left + Width / 2), (int)(Top + 20)));
        var dpi = VisualTreeHelper.GetDpi(this);
        double cx = (screen.Bounds.Left + screen.Bounds.Width / 2.0) / dpi.DpiScaleX;
        double top = screen.Bounds.Top / dpi.DpiScaleY;
        _settings.CenterX = screen.Primary ? null : cx;
        _settings.Top = screen.Primary ? null : top;
        _settings.Save();
        _px.Snap(Left);
        _py.Snap(Top);
        _px.Target = cx - Width / 2;
        _py.Target = _settings.Docked ? MonitorTop() : top;
        _posActive = true;
        StartRendering();
        Kick(3);
    }

    // ================================================================ frame loop

    private void StartRendering()
    {
        if (_rendering) return;
        _rendering = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e) => Measure("frame: island", () => RenderFrame(e));

    private void RenderFrame(EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastFrame) return;
        double dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Min(0.05, (now - _lastFrame).TotalSeconds);
        _lastFrame = now;

        if (!_w.IsSettled || !_h.IsSettled) { CountAnimationFrame(dt); RecordPerf(dt); }
        _w.Step(dt);
        _h.Step(dt);
        _y.Step(dt);
        // volume (Morph island): while the height moves fast the width gives back a little, up to 5 %
        Island.Width = Math.Max(20, _w.Value * (1 + Math.Clamp(-_h.Velocity / 7000, -0.05, 0.05)));
        // Morph island's states are small (36…84 px); this island opens to hundreds of pixels, and folding, the same
        // spring would dip tens of pixels under the pill — for a moment nearly a line. Below its target while
        // shrinking, the height gives way softly (at most about 8 px, the original's size of dip), never under 20.
        double shownH = _h.Value;
        if (_hShrinking && shownH < _h.Target) shownH = _h.Target - 8 * Math.Tanh((_h.Target - shownH) / 8);
        Island.Height = Math.Max(20, shownH);
        IslandShift.Y = _y.Value + _bodyY.Value;

        bool liquid = StepLiquid(dt);

        // the size has arrived: fit the window to it now, even if the outline keeps moving with the music
        if (!_windowFitted && _w.IsSettled && _h.IsSettled)
        {
            _windowFitted = true;
            ShrinkWindowToPill();
            Dispatcher.InvokeAsync(CaptureFrost, System.Windows.Threading.DispatcherPriority.Background);
        }

        if (_w.IsSettled && _h.IsSettled && _y.IsSettled && !liquid)
        {
            CompositionTarget.Rendering -= OnRendering;
            _rendering = false;
            UpdateShape(false);
            ShrinkWindowToPill();
            Dispatcher.InvokeAsync(CaptureFrost, System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    // ---------------------------------------------------------------- window size
    // A see-through (layered) window is copied in full every time anything in it moves, so its size is
    // what the island costs. Folded, the window is only a little taller than the pill (room for the
    // shadow, the glow and the bulge); it grows to full size before the island opens.
    private const double PillRoom = 150;
    private double FullWindowHeight => Math.Ceiling(720 * Math.Max(1, UiScale));

    // ---------------------------------------------------------------- island size (Settings > Appearance)

    private double UiScale => Math.Clamp(_settings.IslandScale, 80, 130) / 100.0;

    /// <summary>Scales the whole island (and its shadow); the window grows with it, keeping its centre.</summary>
    private void ApplyIslandScale()
    {
        var t = new ScaleTransform(UiScale, UiScale);
        t.Freeze();
        Island.LayoutTransform = t;
        ShadowHost.LayoutTransform = t;
        double w = Math.Ceiling(900 * Math.Max(1, UiScale));
        if (Math.Abs(Width - w) > 1)
        {
            double cx = Left + Width / 2;
            Width = w;
            MoveWindow(cx - w / 2, Top);
        }
        GrowWindow();
        ShrinkWindowToPill();
    }

    private bool _windowFitted;

#if DEBUG
    /// <summary>Tests: a window of coloured stripes under the island (in z-order), still or sliding like a video.</summary>
    private Window TestBackdrop(bool moving)
    {
        var stripes = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(80, 30), MappingMode = BrushMappingMode.Absolute, SpreadMethod = GradientSpreadMethod.Repeat };
        stripes.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x5A, 0x5F), 0));
        stripes.GradientStops.Add(new GradientStop(Color.FromRgb(0x2E, 0x9B, 0xFF), 0.5));
        stripes.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xB3, 0x40), 1));
        var slide = new TranslateTransform();
        stripes.Transform = slide;
        var screen = System.Windows.Forms.Screen.FromHandle(_hwnd).Bounds;
        var w = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false, Topmost = true,
            Left = screen.Left / Dpi + screen.Width / Dpi / 2 - 600, Top = screen.Top / Dpi, Width = 1200, Height = 560, Background = stripes,
        };
        w.Show();
        Native.SetWindowPos(_hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);   // the island back on top
        if (moving) slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 160, TimeSpan.FromSeconds(2)) { RepeatBehavior = RepeatBehavior.Forever });
        return w;
    }
#endif

    private static bool PerfNoResize;

#if DEBUG
    /// <summary>Tests: a made-up page under the island: dark, white, colourful or dense text.</summary>
    private Window LookBackdrop(string what)
    {
        Brush bg = what switch
        {
            "dark" => new SolidColorBrush(Color.FromRgb(0x10, 0x12, 0x18)),
            "colour" => new LinearGradientBrush(new GradientStopCollection([new(Color.FromRgb(0xFF, 0x5A, 0x5F), 0), new(Color.FromRgb(0xFF, 0xB3, 0x40), .25), new(Color.FromRgb(0x4C, 0xD9, 0x64), .5), new(Color.FromRgb(0x2E, 0x9B, 0xFF), .75), new(Color.FromRgb(0xAF, 0x52, 0xDE), 1)]), new Point(0, 0), new Point(1, .6)),
            _ => Brushes.White,
        };
        var lines = string.Join(Environment.NewLine, Enumerable.Range(0, 40).Select(i => string.Concat(Enumerable.Repeat($"The quick brown fox jumps over the lazy dog, number {i}. ", 4))));
        var screen = System.Windows.Forms.Screen.FromHandle(_hwnd).Bounds;
        var w = new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false, Topmost = true, Background = bg,
            Left = screen.Left / Dpi + screen.Width / Dpi / 2 - 620, Top = screen.Top / Dpi, Width = 1240, Height = 760,
            Content = what == "text" ? new TextBlock { Text = lines, FontSize = 15, Foreground = Brushes.Black, LineHeight = 20 } : null,
        };
        w.Show();
        Native.SetWindowPos(_hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x13);
        return w;
    }

    /// <summary>
    /// Tests: the contrast of every text on the island against what is really behind it on the screen (the median pixel
    /// of its box: glyphs cover well under half of it).
    /// </summary>
    private string LookContrast(byte[] px, System.Drawing.Rectangle rect)
    {
        var found = new List<(double C, TextBlock T)>();
        var island = new Rect(Island.PointToScreen(new Point(0, 0)), Island.PointToScreen(new Point(Island.ActualWidth, Island.ActualHeight)));
        static double Lin(double v) { v /= 255; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        static double Lum(double cr, double cg, double cb) => 0.2126 * Lin(cr) + 0.7152 * Lin(cg) + 0.0722 * Lin(cb);
        void Walk(DependencyObject d, double opacity)
        {
            if (d is UIElement { IsVisible: false }) return;
            if (d is UIElement u) opacity *= u.Opacity;
            if (opacity < 0.85) return;
            if (d is TextBlock { Foreground: SolidColorBrush fg } t && t.Text.Length > 2 && t.ActualWidth > 8)
            {
                var box = new Rect(t.PointToScreen(new Point(0, 0)), t.PointToScreen(new Point(t.ActualWidth, t.ActualHeight)));
                var lum = new List<double>();
                if (island.Contains(box))
                    for (int y = (int)box.Top - rect.Top; y < (int)box.Bottom - rect.Top; y++)
                        for (int x = (int)box.Left - rect.Left; x < (int)box.Right - rect.Left; x++)
                        {
                            int i = (y * rect.Width + x) * 4;
                            if (x >= 0 && y >= 0 && x < rect.Width && i + 2 < px.Length) lum.Add(Lum(px[i + 2], px[i + 1], px[i]));
                        }
                if (lum.Count > 20)
                {
                    lum.Sort();
                    double b = lum[lum.Count / 2], f = Lum(fg.Color.R, fg.Color.G, fg.Color.B), a = fg.Color.A / 255.0;
                    f = f * a + b * (1 - a);
                    found.Add(((Math.Max(f, b) + 0.05) / (Math.Min(f, b) + 0.05), t));
                }
            }
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++) Walk(VisualTreeHelper.GetChild(d, i), opacity);
        }
        Walk(Island, 1);
        if (found.Count == 0) return "no text";
        found.Sort((a, b) => a.C.CompareTo(b.C));
        string Worst(TextBlock t) => $"{((SolidColorBrush)t.Foreground).Color} {t.FontSize:0.#}px";
        return $"{found.Count} texts, lowest {found[0].C:0.0} ({Worst(found[0].T)}), median {found[found.Count / 2].C:0.0}, under 4.5: {found.Count(f => f.C < 4.5)}, under 3: {found.Count(f => f.C < 3)}";
    }
#endif

    // Resizing a see-through window mid-animation costs a frame or two, so: it grows to full size in one
    // step (best before anything moves: when the cursor reaches the island), and shrinks back only once
    // the island has been folded and quiet for a moment.
    private const double CompactWindowHeight = 240;
    private readonly System.Windows.Threading.DispatcherTimer _shrinkTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };

    private void GrowWindowFor(Size size)
    {
        _windowFitted = false;
        // the frosted picture must cover the new size: take it now for that size (solid only if that fails)
        if ((Math.Abs(size.Width - _w.Target) > 1 || Math.Abs(size.Height - _h.Target) > 1) && !PrecaptureFrost(size)) SetFrostCovers(false);
        _shrinkTimer.Stop();
        double need = Math.Ceiling(IslandTopMargin + Math.Max(size.Height, Island.ActualHeight) * UiScale + PillRoom);
        if (PerfNoResize || need > Height) GrowWindow(need);
    }

    private void GrowWindow(double need = 0)
    {
        // a page taller than usual (the large chat) gets the room it needs, up to the screen
        double h = Math.Min(Math.Max(FullWindowHeight, need), SystemParameters.VirtualScreenHeight);
        if (Height < h) Height = h;
    }

    private void ShrinkWindowToPill()
    {
        if (PerfNoResize) return;
        _shrinkTimer.Stop();
        if (_mode is Mode.Compact or Mode.Toast) _shrinkTimer.Start();
    }

    private void ShrinkNow()
    {
        _shrinkTimer.Stop();
        if (_dragging || _rendering && !(_w.IsSettled && _h.IsSettled) || _mode is not (Mode.Compact or Mode.Toast) || Island.IsMouseOver) return;
        double h = Math.Max(CompactWindowHeight * UiScale, Math.Ceiling(IslandTopMargin + Math.Max(Island.Height, _h.Target) * UiScale + PillRoom));
        h = Math.Min(FullWindowHeight, h);
        if (Math.Abs(Height - h) > 1) Height = h;
    }

    private void MorphTo(Size size)
    {
        GrowWindowFor(size);
        MorphSprings(grow: size.Width * size.Height > _w.Target * _h.Target);
        _w.Target = size.Width;
        _hShrinking = size.Height < _h.Value;
        _h.Target = size.Height;
        StartRendering();
    }

    /// <summary>
    /// The shape's springs, after Morph island (designbookmark.com, tokens.css): width 380 / 24, height 300 / 22.
    /// The width is stiffer, so every change lands sideways first and then drops into place; shrinking, the
    /// height is 1.3× stiffer. "Fast" makes both 1.65× stiffer with the same feel (damping by the square root).
    /// Calm (not bouncy) and Windows' reduced animations: critically damped, as the original does for reduced motion.
    /// </summary>
    private void MorphSprings(bool grow)
    {
        double k = _settings.FastAnimations ? 1.65 : 1, c = Math.Sqrt(k);
        double wk = 380 * k, wc = 24 * c, hk = 300 * k * (grow ? 1 : 1.3), hc = 22 * c;
        if (!_settings.Bouncy || Services.Motion.Reduced) { wc = 2 * Math.Sqrt(wk); hc = 2 * Math.Sqrt(hk); }
        _w.Stiffness = wk; _w.Damping = wc;
        _h.Stiffness = hk; _h.Damping = hc;
    }

    /// <summary>A change of state gives the island a little push (Morph island: more when it grows).</summary>
    private void MorphPush(bool grow)
    {
        if (Services.Motion.Reduced) return;
        _press.Velocity -= grow ? 160 : 100;
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
            // what the content needs, plus what the user pulled the edge out (or in) by
            // (a sung line may be long: the island goes as wide as it needs, up to what fits beside the screen centre)
            double max = Karaoke.Visibility == Visibility.Visible ? 760 : 440;
            return Math.Max(140, Math.Clamp(CompactView.DesiredSize.Width + 8, 200, max) + Math.Clamp(_settings.PillExtra, PillExtraMin, PillExtraMax));
        }
    }

    private RadioButton[] Tabs => [TabHome, TabMusic, TabWeather, TabChat, TabTimer, TabClip, TabNotes, TabCalendar, TabSystem, TabWidgets, TabSettings];

    private FrameworkElement[] AllPages => [HomePage, MusicPage, WeatherPage, ChatPage, TimerPage, ClipPage, NotesPage, CalendarPage, SystemPage, WidgetsPage, SettingsPage];

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
        if (mode == Mode.Expanded && old != Mode.Expanded) HomeMissed.Refresh(fold: true);

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
            MorphOut(v, () => { if (_mode != m) v.Visibility = Visibility.Collapsed; });
        }

        if (old != mode)
        {
            var to = TargetSize(mode);
            MorphPush(grow: to.Width * to.Height > _w.Target * _h.Target);
            if (mode != Mode.Compact) PlayChromatic(mode == Mode.Toast ? 5 : 8); // the "print" shimmer when it opens
            MorphIn(views[mode]);
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
        if (_hidden || _recordDisplayPicker != null || _mode is Mode.Claude or Mode.Drop or Mode.Command) return;
        _collapseDelay.Stop();
        ClearUnread();
        SetMode(Mode.Expanded);
        OnPageShown();
    }

    private void TryCollapse()
    {
        if (_mode == Mode.Drop && _dropWorking) return;   // the zone is still zipping / converting
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
        if (_editMode) return;   // its size is being changed: it stays folded
        _collapseDelay.Stop();
        _shrinkTimer.Stop();
        GrowWindow();   // room to open, before anything moves (resizing mid-animation would stutter)
        if (_mode == Mode.Compact && !_dragging) PrecaptureFrost(TargetSize(Mode.Expanded));   // and the glass's picture for the open size
        if (_mode == Mode.Compact && !_dragging)
        {
            Squish(1.05); // swells a little under the cursor
            Kick(1.2);
            if (_settings.ExpandOnHover) _expandDelay.Start();
        }
    }

    private void Island_MouseLeave(object sender, MouseEventArgs e)
    {
        Release();
        _expandDelay.Stop();
        if (!_dragging) Unsquish(0);
        if (_mode is Mode.Expanded && !CursorOnIsland()) _collapseDelay.Start();
        if (_mode is Mode.Drop && _dropped.Length > 0) _collapseDelay.Start();
        if (_mode == Mode.Toast && !_toastTimer.IsEnabled) _toastTimer.Start();
    }

    /// <summary>
    /// The cursor really is over the island, by its screen position. WPF also reports leaving when the island
    /// changes size under a still cursor (a notification card closed); that must not fold it.
    /// </summary>
    private bool CursorOnIsland()
    {
        if (PresentationSource.FromVisual(Island) == null) return false;
        Native.GetCursorPos(out var p);
        var at = Island.PointFromScreen(new Point(p.X, p.Y));
        return at.X >= 0 && at.Y >= 0 && at.X < Island.ActualWidth && at.Y < Island.ActualHeight;
    }

    private int _tabIndex;


    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (HomePage == null || TabSettings == null) return; // during InitializeComponent
        int index = Array.IndexOf(PageTabs.Append(TabSettings).ToArray(), sender as RadioButton);   // in the order the user sees
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
        else if (TabWidgets.IsChecked == true) RenderWidgetsPage();
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

    /// <summary>Test: the old way (the window trails the cursor on a spring while dragging).</summary>
    private bool DragOnSpring;
    /// <summary>Test: a made-up cursor moving at this speed (DIP per second) from this point, since this time.</summary>
    private (Point From, Vector Speed, DateTime Since)? _testCursor;

    /// <summary>Where the window goes for a cursor position while dragging.</summary>
    private (double X, double Y) DragTarget(Point c)
    {
        var d = c - _pressCursor;
        return (_pressWindow.X + d.X, _pressWindow.Y + (_settings.Docked ? Math.Min(0, d.Y) : d.Y));
    }

    private Point CursorDip()
    {
        if (_testCursor is { } tc) return tc.From + tc.Speed * (DateTime.UtcNow - tc.Since).TotalSeconds;
        Native.GetCursorPos(out var p);
        return new Point(p.X / Dpi, p.Y / Dpi);
    }

    private static bool IsInteractive(DependencyObject? d)
    {
        while (d != null)
        {
            if (d is ButtonBase or TextBoxBase or Slider or ScrollBar or InkCanvas) return true;
            if (d is FrameworkElement fe && fe.Name is "ChatHost" or "MusicWebHost" or "MusicResults" or "ShelfList" or "CommandResults") return true;
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
        Kick(1.4);

        // the squeeze shrinks the island towards its top: a button low in the open island would slide out
        // from under the cursor before the mouse is released, and the click would be lost. Squeeze only
        // the folded pill, and never when a button was pressed.
        bool interactive = IsInteractive(e.OriginalSource as DependencyObject);
        if (!interactive && _mode == Mode.Compact)
        {
            Squish(1 - 0.045 * LiquidK);
            if (!Services.Motion.Reduced) { _press.Target = 96; StartRendering(); }
        }
        if (interactive) return;
        _pressed = true;
        _dragging = false;
        _pressCursor = CursorDip();
        _pressWindow = new Point(Left, Top);
        _samples.Clear();
        _samples.Add((DateTime.Now, _pressCursor));
    }

    private void Island_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_mode != Mode.Compact) return;
        SwitchLive(e.Delta < 0 ? 1 : -1);
        e.Handled = true;
    }

    private void Island_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_pressed || e.LeftButton != MouseButtonState.Pressed) return;
        if (_settings.LockPosition) return;   // pinned in place: no dragging, no swipe-away
        var c = CursorDip();
        var d = c - _pressCursor;
        if (!_dragging)
        {
            if (Math.Abs(d.X) < 6 && Math.Abs(d.Y) < 6) return;
            _dragging = true;
            OnDragStarted();
            _expandDelay.Stop();
            _collapseDelay.Stop();
            Island.CaptureMouse();
            _px.Snap(Left);
            _py.Snap(Top);
            _px.Stiffness = _py.Stiffness = 1500;   // follows the hand closely, without lag or wobble
            _px.Damping = _py.Damping = 72;
            _posActive = true;
        }

        _samples.Add((DateTime.Now, c));
        if (_samples.Count > 16) _samples.RemoveAt(0);

        // the window follows the cursor (each frame, see UpdateLiquid); docked: along the top edge only
        (_px.Target, _py.Target) = DragTarget(c);

        if (Liquid)
        {
            // lean into the motion and stretch along it, like a drop of water
            var v = CursorVelocity();
            _skew.Target = Math.Clamp(-v.X * 0.007, -12, 12) * LiquidK;
            double sx = Math.Clamp(Math.Abs(v.X) * 0.00007, 0, 0.12) * LiquidK, sy = Math.Clamp(Math.Abs(v.Y) * 0.00007, 0, 0.12) * LiquidK;
            _sx.Target = 1 + sx - sy * 0.5;
            _sy.Target = 1 + sy - sx * 0.5;
            _ripple = Math.Min(6, _ripple + v.Length * 0.00035);
        }
        StartRendering();
    }

    private void Island_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        Release();
        if (!_pressed)
        {
            Unsquish(1.6);
            return;
        }
        _pressed = false;

        if (_dragging)
        {
            _dragging = false;
            _px.Stiffness = _py.Stiffness = 560;      // the throw and the settle stay soft
            _px.Damping = _py.Damping = 38;
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
        if (_mode == Mode.Compact && !_editMode) OpenSmart();
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
        if (_yield.Reset()) StopYield(reopen: false);   // the gaze loop rests while hidden: the handle would stay see-through
        UpdateAuraVisibility();
        _posActive = false;
        _settings.Hidden = true;
        if (_placeOwner == null)   // at a program's place: that one is kept already, the usual one stays
        {
            if (Top > SystemParameters.VirtualScreenTop + 1) _settings.Top = Top; // remember where it was
            _settings.CenterX = Left + Width / 2;
        }
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
        UpdateAuraVisibility();
        _settings.Hidden = false;
        _settings.Save();

        Handle.Visibility = Visibility.Collapsed;
        if (_settings.PlacePerApp)   // another program may have come to the front meanwhile
        {
            _placeOwner = _frontApp != null && _settings.AppPlaces.ContainsKey(_frontApp) ? _frontApp : null;
            var (cx, top) = PlaceOf(_placeOwner);
            MoveWindow(cx - Width / 2, top);
        }
        else MoveWindow(Left, _settings.Top ?? MonitorTop());
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
    private int _menusOpen;
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetTopWindow(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);   // context menus of ours that are open right now
    private bool? _testFullscreen;   // tests: pretend a full-screen program is (or isn't) in front

    private void WatchFullscreen()
    {
        // remember where the user was typing, so clipboard items can be pasted back there
        var fg = Native.GetForegroundWindow();
        if (fg != IntPtr.Zero && fg != _hwnd && !OwnsWindow(fg)) _lastExternalWindow = fg;

        // a game or a full-screen program is in front. Either switch needs to know it: "hide in full screen",
        // or "FPS and load in games" (the two exclude each other, so asking only when the first one is on
        // meant the game pill never appeared for anyone who chose FPS)
        _fullscreenFront = _hwnd != IntPtr.Zero && (_testFullscreen ?? Native.IsFullscreenAppActive(_hwnd));   // also: the island steps aside for the cursor
        bool fullscreenApp = (_settings.HideInFullscreen || _settings.GameStats) && _fullscreenFront;
        // game mode: keep a tiny stats pill instead of hiding completely
        bool game = _demoGame || (fullscreenApp && _settings.GameStats && IsGameWindow(fg));
        // with the FPS switch on, a full-screen program that isn't a game (a browser video, a slideshow) gets "hide in full screen", which the FPS switch stands in for
        bool fullscreen = fullscreenApp && (_settings.HideInFullscreen || (_settings.GameStats && !game));
        if (game != _gameMode)
        {
            _gameMode = game;
            OnGameModeChanged(game);
            if (_mode == Mode.Compact) MorphTo(TargetSize(Mode.Compact));
        }
        // also hide in front of the programs chosen in Settings (exe names)
        bool fs = (fullscreen && !game) || IsHiddenApp(fg) || _demoHide;
        SetGameFront(game || _fullscreenFront && !Native.IsZoomed(fg) && IsGameWindow(fg, onlyListed: false));   // (a maximized window over a hidden taskbar is no game)
        if (fs != _fullscreenHidden)
        {
            _fullscreenHidden = fs;
            UpdateAuraVisibility();
            CaptureFrost();
            if (fs)
            {
                if (_yield.Reset()) ClickThrough(false);
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
        // a menu of ours is open: pulling the island to the very top now would put it over that menu
        if (!fs && !_dragging && _menusOpen == 0 && _recordDisplayPicker == null && !(_gameFront && Native.IsAbove(_hwnd, fg))) Native.KeepTopmost(_hwnd);
        if (!fs) WatchFrontApp(fg);
    }

    // ================================================================ clock & eyes

    private void OnTick()
    {
        _tickCount++;
        UiStep("tick");
        var now = _demoClock ?? DateTime.Now.ToString(ClockFormat, System.Globalization.CultureInfo.InvariantCulture);
        ExpandedTime.Text = now;
        if (_recording.IsRecording)
        {
            var t = DateTime.Now - _recording.StartedAt;
            CompactTime.Text = $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
            CompactTime.Foreground = (Brush)FindResource("Danger");
        }
        else
        {
            CompactTime.Text = PillClockText(now);
            CompactTime.SetResourceReference(TextBlock.ForegroundProperty, "Fg");
        }

        UpdateProgress();
        KeepRepeating();
        WatchKeyboard();
        Measure("brightness", WatchBrightness);
        Measure("lyrics line", UpdateLyricLine);
        UpdateLive();
        if (_tickCount % 4 == 0)
        {
            Measure("slow tick", SlowTick);
            if (_tickCount % 8 == 0) _media.PollLegacy();
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
        UpdatePillRight();
    }

    private readonly List<Views.CharacterView> _previewCharacters = [];
    private Native.POINT? _fakeCursor;

    /// <summary>~30 times a second: drive the character and the pill's reaction to the cursor.</summary>
    private readonly Dictionary<Views.CharacterView, (Point Center, long Tick)> _charCenters = [];
    private long _gazeTicks;
    private DateTime _gazeAt = DateTime.Now;

    private void UpdateGaze()
    {
        if (!IsLoaded || _hidden || _fullscreenHidden) return;
        _gazeTicks++;
        Native.GetCursorPos(out var cursor);
        if (_fakeCursor is Native.POINT fake) cursor = fake;   // screenshots / tests
        CheckClickOutside(cursor);
        UpdateYield(cursor);
        if (_mode == Mode.Compact && !_rendering && _gazeTicks % 3 != 0)
        {
            double dx = cursor.X - (Left + Width / 2) * Dpi, dy = cursor.Y - Top * Dpi;
            if (dx * dx + dy * dy > 420 * 420) return;
        }
        var gazeNow = DateTime.Now;
        double gazeDt = Math.Clamp((gazeNow - _gazeAt).TotalSeconds, 0.001, 0.15);   // real time since the last frame
        _gazeAt = gazeNow;
        bool playing = _lastMedia?.IsPlaying == true;
        double beat = playing ? Math.Min(1, _audio.Peak * 1.6) : 0;
        var mood = _demoSleep || (_demoMedia == null && Native.IdleSeconds() > 120) ? Views.CharacterMood.Sleep
                 : _claudeCurrent != null ? Views.CharacterMood.Think
                 : playing ? Views.CharacterMood.Music : Views.CharacterMood.Idle;

        void Drive(Views.CharacterView ch, Views.CharacterMood? moodOverride = null)
        {
            if (!ch.IsVisible) return;
            if (!_charCenters.TryGetValue(ch, out var cached) || _gazeTicks - cached.Tick > 15 || _rendering)
            {
                try { cached = (ch.PointToScreen(new Point(ch.ActualWidth / 2, ch.ActualHeight / 2)), _gazeTicks); }
                catch { return; }
                _charCenters[ch] = cached;
            }
            var c = cached.Center;
            ch.LookX = Math.Clamp((cursor.X - c.X) / 170.0, -1, 1);
            ch.LookY = Math.Clamp((cursor.Y - c.Y) / 170.0, -1, 1);
            ch.Beat = beat;
            ch.Mood = moodOverride ?? mood;
            ch.Tint = _albumColor;
            ch.Sketch = _settings.HandDrawn;
            ch.Tick(gazeDt);
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
        bool menu = _mode == Mode.Expanded && !buddy && _settings.Character != "none";
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
        if (_mode == Mode.Expanded && TabMusic.IsChecked == true) UpdateMusicWave(gazeDt);
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

    private Views.NotificationStack.Item? _toastKept;
    private Views.SideToast? _sideToast;

    /// <param name="keep">A notification (a message, Claude, a timer, a download…), not an answer to what the user just
    /// did: it also goes on the Home page's stack, shown or not.</param>
    public void ShowToast(string glyph, string title, string? text, Action? onClick = null, Brush? badge = null, int ms = 2600, bool keep = false)
    {
        Views.NotificationStack.Item? kept = null;
        if (keep)
        {
            kept = new(glyph, badge, title, text, DateTime.Now, onClick);
            HomeMissed.Add(kept);
        }
        if (_mode == Mode.Claude || _hidden) return;
        // the island is open: the notice goes beside it, the open island stays as it is (folding it for a notice
        // threw away what the user was doing there)
        if (_mode == Mode.Expanded && IsLoaded && PresentationSource.FromVisual(Island) != null)
        {
            _sideToast ??= new Views.SideToast { HideFromCapture = HideFromCapture };
            var tl = Island.PointToScreen(new Point(0, 0));
            var br = Island.PointToScreen(new Point(Island.ActualWidth, Island.ActualHeight));
            var kept2 = kept;
            _sideToast.Show(new Rect(tl, br), glyph, badge, title, text, onClick == null && kept2 == null ? null : () =>
            {
                if (kept2 != null) HomeMissed.Forget(kept2);   // seen and acted on: not missed
                onClick?.Invoke();
            }, ms);
            Character.Cheer();
            MenuBuddy.Cheer();
            return;
        }
        _toastKept = kept;
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
        if (_toastKept != null) HomeMissed.Forget(_toastKept);   // seen and acted on: not missed
        _toastKept = null;
        SetMode(Mode.Compact);
        a?.Invoke();
    }

    // ================================================================ theme

    private void ApplyTheme()
    {
        Theme.Apply(_settings.DarkTheme, _settings.IslandColor, _settings.GlassOpacity, _settings.AccentColor);
        ApplyLiquidBrushes();
    }

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
        bool liquid = GlassScene;
        IslandShadow.Opacity = _settings.DarkTheme && !liquid ? 0.38 : 0.18;
        IslandShadow.BlurRadius = liquid ? 24 : 34;
        IslandShadow.ShadowDepth = liquid ? 8 : 6;
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
            Text = Settings.Safe ? "Atoll · " + L.T("безопасный режим") : "Atoll",
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip(),
        };
        _tray.ContextMenuStrip.Items.Add(L.T("Показать / спрятать  (Ctrl+Alt+I)"), null, (_, _) => ToggleHidden());
        _tray.ContextMenuStrip.Items.Add(L.T("Скриншот области  (Ctrl+Alt+S)"), null, (_, _) => _ = RunAction("region"));
        _tray.ContextMenuStrip.Items.Add(L.T("Выровнять по центру"), null, (_, _) => AlignIsland());
        _tray.ContextMenuStrip.Items.Add(L.T("Сменить тему  (Ctrl+Alt+T)"), null, (_, _) => ToggleTheme());
        _tray.ContextMenuStrip.Items.Add(L.T("Изменить виджеты"), null, (_, _) => EditWidgets(true));
        _tray.ContextMenuStrip.Items.Add(L.T("Добавить виджет…"), null, (_, _) => EditWidgets(true, gallery: true));
        var pauseItem = new Forms.ToolStripMenuItem(L.T("Пауза виджетов")) { CheckOnClick = false };
        pauseItem.Click += (_, _) => ToggleWidgetsPaused();
        _tray.ContextMenuStrip.Opening += (_, _) => pauseItem.Checked = _settings.WidgetsPaused;
        _tray.ContextMenuStrip.Items.Add(pauseItem);
        _tray.ContextMenuStrip.Items.Add(L.T("Настройки"), null, (_, _) => OpenTab(TabSettings));
        _tray.ContextMenuStrip.Items.Add(new Forms.ToolStripSeparator());
        _tray.ContextMenuStrip.Items.Add(L.T("Выход"), null, (_, _) => Close());
        _tray.MouseClick += (_, a) => { if (a.Button == Forms.MouseButtons.Left) ToggleHidden(); };
    }
}

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DynamicIsland.Services;

public sealed class WidgetTodo
{
    public string Text { get; set; } = "";
    public bool Done { get; set; }
}

public sealed class Reminder
{
    public string Title { get; set; } = "";
    public DateTime When { get; set; }
    public bool Notified { get; set; }
}

public sealed class CustomTheme
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Dark { get; set; } = true;
    public string Island { get; set; } = "#000000";
    public string Accent { get; set; } = "#30D158";
    public string Aura { get; set; } = "none";
    public string Scene { get; set; } = "";
}

public sealed class Settings
{
    /// <summary>No settings file yet: the very first start on this computer.</summary>
    public static bool FirstRun { get; private set; }

    private static readonly Lazy<Settings> _current = new(Load);
    /// <summary>The settings of this run, read on first use (tests never read the user's file).</summary>
    public static Settings Current => _current.Value;

    // weather
    public string City { get; set; } = "";

    // position: island centre X and window top, in DIPs (null = top centre of primary screen)
    public double? CenterX { get; set; }
    public double? Top { get; set; }
    public bool Hidden { get; set; }

    // look
    public bool DarkTheme { get; set; } = true;
    public string IslandColor { get; set; } = "#000000";
    /// <summary>Accent colour of the chosen theme ("" = the default green).</summary>
    public string AccentColor { get; set; } = "";
    public string ThemeId { get; set; } = "";
    /// <summary>The animated scene of a season theme: spring, summer, autumn, winter (empty: none).</summary>
    public string Scene { get; set; } = "";
    /// <summary>Your own photo inside the island (a copy is kept in the settings folder).</summary>
    public bool IslandPhoto { get; set; }
    /// <summary>1 = solid (readable). Kept for old settings files.</summary>
    public double GlassOpacity { get; set; } = 1;
    /// <summary>Jelly / ripple physics (off = calm, smooth animation).</summary>
    public bool LiquidWaves { get; set; }
    /// <summary>How strong the live waves are, 10…100.</summary>
    public int LiquidStrength { get; set; } = 60;
    /// <summary>Which creature lives in the pill (see CharacterView.All).</summary>
    public string Character { get; set; } = "eyes";
    /// <summary>Edge reaches for the cursor, breathes with music, glows on events.</summary>
    public bool Reactive { get; set; } = true;
    /// <summary>The edge moves with the music (drips with the waves, a swell with live response); off = only touches move it.</summary>
    public bool MusicWaves { get; set; } = true;

    /// <summary>Orange light under the island while a Claude character is on.</summary>
    public bool ClaudeGlow { get; set; } = true;
    /// <summary>Characters look hand-drawn: ink outlines that "boil", animated on twos (12 fps).</summary>
    public bool HandDrawn { get; set; }
    /// <summary>Frosted glass: the desktop behind the island, blurred, under a tint.</summary>
    public bool Glass { get; set; } = true;
    public bool ShowCapsLock { get; set; } = true;
    /// <summary>Volume and screen brightness: a level beside the island while they change.</summary>
    public bool ShowLevels { get; set; } = true;
    /// <summary>Copying, unpacking, rendering… (programs that show a percentage in their window title) as a live activity.</summary>
    public bool ShowProgress { get; set; } = true;
    /// <summary>The island moves to the monitor the mouse is on.</summary>
    public bool FollowMonitor { get; set; }
    /// <summary>Extra width of the folded pill (pulled by its edge), in pixels; negative makes it narrower.</summary>
    public int PillExtra { get; set; }
    /// <summary>The middle of the idle pill: "time", "timedate" or "date".</summary>
    public string PillClock { get; set; } = "time";
    /// <summary>The right side of the idle pill: "weather", "battery" or "none".</summary>
    public string PillRight { get; set; } = "weather";
    /// <summary>The user's order of the page tabs (names), and the ones switched off.</summary>
    public List<string> TabOrder { get; set; } = [];
    public List<string> HiddenTabs { get; set; } = [];
    /// <summary>How round the corners are, 0 (nearly square) … 100 (a pill).</summary>
    public int Roundness { get; set; } = 100;
    /// <summary>How solid the folded island is while the cursor is away, 30 … 100 percent.</summary>
    public int IdleOpacity { get; set; } = 100;
    /// <summary>The shadow under the island.</summary>
    public bool ShowShadow { get; set; } = true;
    /// <summary>A ☰ button in the open island's header for the island's menu (besides the right click).</summary>
    public bool MenuButton { get; set; }
    /// <summary>Font family for the whole island ("" = the system one).</summary>
    public string Font { get; set; } = "";
    /// <summary>The clock: 12-hour, with seconds.</summary>
    public bool Clock12 { get; set; }
    public bool ClockSeconds { get; set; }
    /// <summary>How long the cursor rests on the pill before it opens, in milliseconds.</summary>
    public int HoverDelay { get; set; } = 100;
    /// <summary>The chime of a finished timer, a reminder, an event.</summary>
    public bool Sounds { get; set; } = true;
    /// <summary>The welcome steps have been shown.</summary>
    public bool Onboarded { get; set; }
    /// <summary>The version whose "what's new" the user has seen.</summary>
    public string SeenVersion { get; set; } = "";
    /// <summary>Themes the user made: id, name, colours, aura, scene.</summary>
    public List<CustomTheme> CustomThemes { get; set; } = [];
    /// <summary>The chat page in its large size (sign-in pages and captchas fit).</summary>
    public bool ChatLarge { get; set; }
    /// <summary>A Discord call as a little island to the right of the main one (instead of inside the pill).</summary>
    public bool DiscordSidePill { get; set; } = true;
    /// <summary>Off: a Discord call shows nowhere on the island (neither beside it nor inside the pill).</summary>
    public bool ShowDiscordCall { get; set; } = true;
    /// <summary>The smart island: opens on what matters now, learns your habits, keeps quiet while you type.</summary>
    public bool SmartIsland { get; set; } = true;
    /// <summary>How often each tab was used, per hour of the day (24 counts per tab). Stays on this PC.</summary>
    public Dictionary<string, int[]> TabHabits { get; set; } = [];
    /// <summary>Keep the island out of screenshots, recordings and screen sharing (Zoom, Discord, OBS).</summary>
    /// <remarks>
    /// null = automatic: hidden, except while NVIDIA's recorder (ShadowPlay / NVIDIA app overlay) runs — it refuses
    /// to record the desktop when any window hides itself ("Atoll prevents desktop recording").
    /// </remarks>
    public bool? CaptureHidden { get; set; }
    public bool ShowKeyboardLayout { get; set; } = true;
    /// <summary>The island stays where it is: no dragging, no swipe-away.</summary>
    public bool LockPosition { get; set; }
    /// <summary>Size of the island, 80…130 %.</summary>
    public int IslandScale { get; set; } = 100;
    /// <summary>Programs (exe names without .exe) in front of which the island hides.</summary>
    public List<string> HideInApps { get; set; } = [];
    /// <summary>Shortcut that opens / folds the island: "Ctrl+Alt+O", "Ctrl+Alt+A", "Alt+Q", "Ctrl+Shift+Space" or "" (off).</summary>
    public string OpenHotkey { get; set; } = "Ctrl+Alt+O";
    /// <summary>Shortcut of the command bar: "Alt+Space" (with Ctrl+Alt+Space), "Ctrl+Alt+Space", "Ctrl+Shift+Space" or "" (off).</summary>
    public string CommandHotkey { get; set; } = "Alt+Space";
    /// <summary>After a screenshot: false = open the editor, true = save it and copy it, only a short notice.</summary>
    public bool ScreenshotQuick { get; set; }
    /// <summary>Show the saved picture in File Explorer.</summary>
    public bool ScreenshotReveal { get; set; }


    /// <summary>Tracks liked with the heart on the music page ("Artist — Title"), newest first.</summary>
    public List<string> LikedTracks { get; set; } = [];
    /// <summary>Where each liked track was playing (a service's name, see MusicSources): a click opens it there.</summary>
    public Dictionary<string, string> LikedFrom { get; set; } = [];
    /// <summary>Light graphics for weak PCs: no frosted glass, glow, colour split or bulge. Null = decide by the hardware.</summary>
    public bool? LiteGraphics { get; set; }
    /// <summary>Tint strength over the frosted glass, 20…100 %.</summary>
    public int GlassTint { get; set; } = 72;
    /// <summary>Liquid glass: from clear (0) to nearly opaque (100). Its own value: GlassTint belongs to the other themes.</summary>
    public int LiquidTint { get; set; } = 50;
    /// <summary>The same for the "Полупрозрачная" and "Матовая" themes.</summary>
    public int ClearTint { get; set; } = 70;
    public int MatteTint { get; set; } = 50;
    /// <summary>Lower half of the island fades to see-through.</summary>
    public bool BottomFade { get; set; } = true;
    /// <summary>Shimmering colour along the lower edge when idle: none, aurora, sunset, ocean, neon, mono.</summary>
    public string Aura { get; set; } = "aurora";
    /// <summary>Panels land with a small springy hop.</summary>
    public bool Bouncy { get; set; } = true;
    public bool FastAnimations { get; set; }
    /// <summary>Header: "hover" = page dots that turn into icons under the cursor, "always" = icons.</summary>
    public string TabIcons { get; set; } = "hover";
    public int Version { get; set; }

    // live activities & extras
    public bool GameStats { get; set; }
    /// <summary>The FPS pill only for the programs in <see cref="GameStatsApps"/>, not for every full-screen one.</summary>
    public bool GameStatsOnlyListed { get; set; }
    /// <summary>Programs (exe names without .exe) that get the FPS pill when <see cref="GameStatsOnlyListed"/> is on.</summary>
    public List<string> GameStatsApps { get; set; } = [];
    public bool Karaoke { get; set; } = true;
    public bool MirrorNotifications { get; set; } = true;
    public bool MirrorAllApps { get; set; }
    public string CalendarIcs { get; set; } = "";
    /// <summary>Files parked on the shelf (paths only, the files stay where they are).</summary>
    public List<string> Shelf { get; set; } = [];
    public List<Reminder> Reminders { get; set; } = [];
    public bool PomodoroFocus { get; set; } = true;
    /// <summary>Pomodoro phases, minutes, and the sessions finished today.</summary>
    public int PomodoroWorkMin { get; set; } = 25;
    public int PomodoroBreakMin { get; set; } = 5;
    public int PomodoroLongMin { get; set; } = 15;
    public DateTime PomodoroCountedOn { get; set; }
    public int PomodoroDone { get; set; }
    public List<Alarm> Alarms { get; set; } = [];
    /// <summary>The clock widget: face "classic", "minimal" or "digital"; the open tab of the large one.</summary>
    public string ClockFace { get; set; } = "classic";
    public string ClockTab { get; set; } = "clock";
    /// <summary>The seconds hand sweeps smoothly (about 1% CPU per face) or jumps once a second (almost free).</summary>
    public bool ClockSweep { get; set; } = true;
    /// <summary>Widgets "on top of everything" instead of on the desktop (default: on the desktop).</summary>
    public bool WidgetsOnTop { get; set; }
    /// <summary>The tray's "pause": widgets stop animating and polling until resumed.</summary>
    public bool WidgetsPaused { get; set; }
    /// <summary>The weather widget's slow background (mist, drops, snow, a warm glare).</summary>
    public bool WeatherAtmosphere { get; set; } = true;
    /// <summary>The note on the note widget (by when it was made; null = the newest).</summary>
    public DateTime? StickyNote { get; set; }
    /// <summary>The photo widget: "folder", "single" or "thisday"; its folder ("" = Pictures) or file; slideshow settings.</summary>
    public string PhotoMode { get; set; } = "folder";
    public string PhotoFolder { get; set; } = "";
    public string PhotoFile { get; set; } = "";
    public int PhotoIntervalMin { get; set; } = 5;
    public bool PhotoShuffle { get; set; } = true;
    public bool PhotoSubfolders { get; set; } = true;
    public bool PhotoKenBurns { get; set; } = true;
    /// <summary>Focus points picked by hand: file → (x, y), 0..1 of the picture.</summary>
    public Dictionary<string, double[]> PhotoFocus { get; set; } = [];
    public string RestoreToasts { get; set; } = ""; // "1"/"0" while our "Не беспокоить" is on

    // behaviour
    public bool HideInFullscreen { get; set; } = true;
    /// <summary>Attached to the top edge of the screen like a notch (true) or floating pill (false).</summary>
    public bool Docked { get; set; }
    public bool ExpandOnHover { get; set; } = true;
    /// <summary>A cursor flying onto the island over another window (a game, a hand on its way to the tabs) goes through it.</summary>
    public bool YieldToCursor { get; set; } = true;
    /// <summary>The island remembers where it was moved for each program in front, and floats there.</summary>
    public bool PlacePerApp { get; set; }
    /// <summary>Process name (lower case) → [centre x, top] in DIPs.</summary>
    public Dictionary<string, double[]> AppPlaces { get; set; } = [];
    /// <summary>Attached to a side ("left", "right") or to the taskbar ("taskbar"); "" floats or sits at the top edge (Docked).</summary>
    public string Attach { get; set; } = "";
    /// <summary>Attached: the island's centre when it was put there (DIP): which screen, and where along the edge.</summary>
    public double[]? AttachAt { get; set; }

    // Home tab, "build it yourself"
    public bool HomeActions { get; set; } = true;
    public bool HomeMusic { get; set; } = true;
    public bool HomeWeather { get; set; } = true;
    public List<string> Actions { get; set; } =
        ["region", "full", "ocr", "qr", "picker", "cleantext", "record", "command"];

    // actions
    public string Command { get; set; } = "";
    public string TranslateTo { get; set; } = "";
    /// <summary>Where "Translate" goes: the chosen language, or the interface language when not set.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string TranslateTarget => string.IsNullOrWhiteSpace(TranslateTo) ? Loc.Lang : TranslateTo;
    /// <summary>Interface language: "" = like Windows, or en / ru / uk / es / de / pt / zh.</summary>
    public string Language { get; set; } = "";
    /// <summary>Look for a newer release on GitHub now and then.</summary>
    public bool AutoUpdateCheck { get; set; } = true;
    /// <summary>Apps pinned to the row at the bottom of Home.</summary>
    public List<string> DockApps { get; set; } = [];
    public bool HomeApps { get; set; } = true;
    /// <summary>Clean desktop: hide the desktop icons (undone on exit). The taskbar is left alone.</summary>
    public bool CleanDesktop { get; set; }
    public bool TaskbarWasAutoHide { get; set; }
    public bool IconsWereVisible { get; set; } = true;
    public bool RecordSystemAudio { get; set; } = true;
    public bool RecordMicrophone { get; set; } = true;

    /// <summary>The source used last in the Music tab (see MusicPlayers).</summary>
    public string MusicSource { get; set; } = "";
    public string MusicFolder { get; set; } = "";
    public List<RadioDirectory.Station> RadioLiked { get; set; } = [];

    // chat
    public string ChatProvider { get; set; } = "gemini";
    /// <summary>When a real event last came from Claude Code's / Codex's hook (null = never since the hooks were set up).</summary>
    public DateTime? ClaudeLastEvent { get; set; }
    public DateTime? CodexLastEvent { get; set; }
    private string _geminiKey = "", _geminiKeySealed = "";

    /// <summary>The Gemini key itself: kept in memory only, never written as text (see <see cref="GeminiVoiceApiKeySealed"/>).</summary>
    [JsonIgnore]
    public string GeminiVoiceApiKey
    {
        get => _geminiKey;
        set
        {
            value ??= "";
            if (value == _geminiKey) return;
            _geminiKey = value;
            _geminiKeySealed = Seal(value);
            Log.Hide(value);
        }
    }

    /// <summary>The Gemini key as written to the file: encrypted for this Windows user (DPAPI), base64.</summary>
    public string GeminiVoiceApiKeySealed
    {
        get => _geminiKeySealed;
        set
        {
            _geminiKeySealed = value ?? "";
            _geminiKey = Unseal(_geminiKeySealed);
            Log.Hide(_geminiKey);
        }
    }

    private static string Seal(string text) => text.Length == 0 ? ""
        : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(text), null, DataProtectionScope.CurrentUser));

    private static string Unseal(string sealedText)
    {
        if (sealedText.Length == 0) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(sealedText), null, DataProtectionScope.CurrentUser)); }
        catch (Exception ex)
        {
            // another Windows user's or another PC's file: the key has to be entered again
            Log.Write("settings: the Gemini key can't be decrypted (" + ex.GetType().Name + ")");
            return "";
        }
    }
    /// <summary>Desktop widgets that are on, and where each one sits (left, top in DIPs).</summary>
    public List<string> DesktopWidgets { get; set; } = [];
    public Dictionary<string, double[]> WidgetPlaces { get; set; } = [];
    /// <summary>The look of all widgets, and the widgets that have a look of their own.</summary>
    public string WidgetTheme { get; set; } = "liquid";
    public Dictionary<string, string> WidgetThemeOf { get; set; } = [];
    public bool WidgetLiveGlass { get; set; } = true;
    public bool WidgetsHidden { get; set; }
    public List<WidgetTodo> WidgetTodos { get; set; } = [];
    public string WaterDay { get; set; } = "";
    public int WaterCount { get; set; }
    public int WaterGoal { get; set; } = 8;
    public string CountdownTitle { get; set; } = "";
    public DateTime? CountdownDate { get; set; }
    /// <summary>Per widget: its size (S, M, L), accent (hex), how much glass (0..1); the world clock's cities.</summary>
    public Dictionary<string, string> WidgetSizeOf { get; set; } = [];
    public Dictionary<string, string> WidgetAccentOf { get; set; } = [];
    public Dictionary<string, double> WidgetGlassOf { get; set; } = [];
    public bool WidgetTipShown { get; set; }
    /// <summary>The glass of the widgets: how much the rim bends the picture (0..1) and how soft the blur is (px).</summary>
    public double GlassRefraction { get; set; } = 0.6;
    public double GlassBlur { get; set; } = 36;
    /// <summary>The widget grid: "A" (cell 72, gap 16) or "B" (cell 76, gap 12); its version (2 = cells, S 2×2…).</summary>
    public string WidgetGrid { get; set; } = "A";
    /// <summary>The music widget's visualiser: "wave", "bars", "ring" or "off".</summary>
    public string MusicViz { get; set; } = "wave";
    /// <summary>Lyrics sync shift, ms (−500…+500, step 50): more = the lines come earlier.</summary>
    public int LyricsOffsetMs { get; set; }
    /// <summary>Equaliser (through Equalizer APO): 10 bands in dB, ±12.</summary>
    public double[] EqGains { get; set; } = new double[10];
    public bool EqBypass { get; set; }
    /// <summary>The preset the bands came from ("" once a band is moved by hand).</summary>
    public string EqPreset { get; set; } = "Плоский";
    public Dictionary<string, double[]> EqCustom { get; set; } = [];
    public int WidgetGridVersion { get; set; }
    /// <summary>Where the widgets are, per set of monitors (key: positions and resolutions of the monitors).</summary>
    public Dictionary<string, Dictionary<string, double[]>> WidgetLayouts { get; set; } = [];
    public List<string> WorldCities { get; set; } = ["Нью-Йорк", "Лондон", "Токио", "Париж", "Дубай", "Сидней"];
    public string GeminiVoice { get; set; } = "Kore";
    public int GeminiVoiceVolume { get; set; } = 100;

    /// <summary>The settings file; tests point it elsewhere.</summary>
    internal static string FilePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DynamicIsland", "settings.json");

    private const int CurrentVersion = 3;

    internal static Settings Load()
    {
        Settings s;
        bool existed = File.Exists(FilePath);
        FirstRun = !existed;
        string? plainKey = null;
        try
        {
            if (existed)
            {
                var text = File.ReadAllText(FilePath);
                s = JsonSerializer.Deserialize<Settings>(text) ?? new Settings();
                // up to 2.0.0 the Gemini key was written as plain text
                plainKey = (JsonNode.Parse(text) as JsonObject)?["GeminiVoiceApiKey"]?.GetValue<string>();
            }
            else s = new Settings();
        }
        catch (Exception ex)
        {
            Log.Write("settings: unreadable, starting fresh (" + ex.GetType().Name + ")");
            s = new Settings();
        }
        if (!string.IsNullOrEmpty(plainKey))
        {
            if (s.GeminiVoiceApiKey.Length == 0) s.GeminiVoiceApiKey = plainKey;
            s.Save();   // written again without the plain key
        }

        if (existed) s.Onboarded = true;   // people who already use it never get the welcome steps
        if (s.Version < CurrentVersion)
        {
            // v3: glass was unreadable and the waves too much → solid & calm
            s.GlassOpacity = 1;
            s.LiquidWaves = false;
            s.Version = CurrentVersion;
        }
        return s;
    }

    // ------------------------------------------------------------------ --safe

    /// <summary>--safe: what 1.8.0 added is off for this run. Only in memory: the file keeps the user's values.</summary>
    public static bool Safe { get; private set; }

    /// <summary>Per setting: the user's own value and the one --safe put in its place.</summary>
    private Dictionary<string, (JsonNode? Own, JsonNode? Safe)>? _safeSwaps;

    public void EnterSafeMode()
    {
        Safe = true;
        var own = JsonSerializer.SerializeToNode(this) as JsonObject;
        ShowLevels = false;
        ShowProgress = false;
        FollowMonitor = false;
        Karaoke = false;
        PillExtra = 0;
        Roundness = 100;
        IdleOpacity = 100;
        Font = "";
        GeminiVoiceApiKey = "";
        Onboarded = true;
        SeenVersion = Updater.Current.ToString(3);
        var safe = JsonSerializer.SerializeToNode(this) as JsonObject;
        _safeSwaps = [];
        if (own == null || safe == null) return;
        foreach (var (name, value) in safe)
            if (!JsonNode.DeepEquals(own[name], value)) _safeSwaps[name] = (own[name]?.DeepClone(), value?.DeepClone());
        Log.Write("safe mode: " + (_safeSwaps.Count == 0 ? "nothing to switch off" : "off for this run: " + string.Join(", ", _safeSwaps.Keys)));
    }

    /// <summary>Demo / screenshot mode: never touch the user's files.</summary>
    public static bool ReadOnly { get; set; }

    private static readonly object WriteLock = new();
    private Timer? _saveSoon;
    private int _saveSoonPending;

    public void Save()
    {
        Interlocked.Exchange(ref _saveSoonPending, 0);
        _saveSoon?.Change(Timeout.Infinite, Timeout.Infinite);
        if (ReadOnly) return;
        Write(Snapshot());
    }

    /// <summary>
    /// Saves after 500 ms without changes, off the UI thread, the latest state: for sliders and scrubbers, which
    /// change many times a second (each change used to write the file).
    /// </summary>
    public void SaveSoon()
    {
        if (ReadOnly) return;
        Interlocked.Exchange(ref _saveSoonPending, 1);
        _saveSoon ??= new Timer(_ => SaveSoonNow());
        _saveSoon.Change(500, Timeout.Infinite);
    }

    /// <summary>A pending <see cref="SaveSoon"/> is written now (on exit).</summary>
    public void Flush()
    {
        if (Interlocked.Exchange(ref _saveSoonPending, 0) == 1) Save();
    }

    private void SaveSoonNow()
    {
        if (Interlocked.Exchange(ref _saveSoonPending, 0) == 0) return;
        // the values are read where they are changed (the UI thread), the file is written here
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        string? json = null;
        try { json = dispatcher == null || dispatcher.CheckAccess() ? Snapshot() : dispatcher.Invoke(Snapshot); }
        catch (Exception ex) { Log.Write("settings: save soon: " + ex.GetType().Name); }
        if (json != null) Write(json);
    }

    private string Snapshot()
    {
        var json = JsonSerializer.SerializeToNode(this) as JsonObject ?? [];
        // a value --safe changed goes back to the user's own, unless the user changed it in this run
        foreach (var (name, (own, safe)) in _safeSwaps ?? [])
            if (JsonNode.DeepEquals(json[name], safe)) json[name] = own?.DeepClone();
        return json.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static void Write(string json)
    {
        lock (WriteLock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                // write to a temp file and swap: a kill in the middle can't leave a half-written settings file
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, FilePath, overwrite: true);
            }
            catch (Exception ex) { Log.Write("settings: not saved (" + ex.GetType().Name + ")"); }
        }
    }
}

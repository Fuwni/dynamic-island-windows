using System.IO;
using System.Text.Json;

namespace DynamicIsland.Services;

public sealed class Reminder
{
    public string Title { get; set; } = "";
    public DateTime When { get; set; }
    public bool Notified { get; set; }
}

public sealed class Settings
{
    public static Settings Current { get; } = Load();

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
    /// <summary>Orange light under the island while a Claude character is on.</summary>
    public bool ClaudeGlow { get; set; } = true;
    /// <summary>Characters look hand-drawn: ink outlines that "boil", animated on twos (12 fps).</summary>
    public bool HandDrawn { get; set; }
    /// <summary>Frosted glass: the desktop behind the island, blurred, under a tint.</summary>
    public bool Glass { get; set; } = true;
    public bool ShowCapsLock { get; set; } = true;
    /// <summary>The chat page in its large size (sign-in pages and captchas fit).</summary>
    public bool ChatLarge { get; set; }
    /// <summary>Keep the island out of screenshots, recordings and screen sharing (Zoom, Discord, OBS).</summary>
    public bool HideFromCapture { get; set; } = true;
    public bool ShowKeyboardLayout { get; set; } = true;
    /// <summary>The island stays where it is: no dragging, no swipe-away.</summary>
    public bool LockPosition { get; set; }
    /// <summary>Size of the island, 80…130 %.</summary>
    public int IslandScale { get; set; } = 100;
    /// <summary>Programs (exe names without .exe) in front of which the island hides.</summary>
    public List<string> HideInApps { get; set; } = [];
    /// <summary>Shortcut that opens / folds the island: "Ctrl+Alt+O", "Ctrl+Alt+A", "Alt+Q", "Ctrl+Shift+Space" or "" (off).</summary>
    public string OpenHotkey { get; set; } = "Ctrl+Alt+O";
    /// <summary>Tracks liked with the heart on the music page ("Artist — Title"), newest first.</summary>
    public List<string> LikedTracks { get; set; } = [];
    /// <summary>Light graphics for weak PCs: no frosted glass, glow, colour split or bulge. Null = decide by the hardware.</summary>
    public bool? LiteGraphics { get; set; }
    /// <summary>Tint strength over the frosted glass, 20…100 %.</summary>
    public int GlassTint { get; set; } = 72;
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
    public bool Karaoke { get; set; } = true;
    public bool MirrorNotifications { get; set; } = true;
    public bool MirrorAllApps { get; set; }
    public string CalendarIcs { get; set; } = "";
    /// <summary>Files parked on the shelf (paths only, the files stay where they are).</summary>
    public List<string> Shelf { get; set; } = [];
    public List<Reminder> Reminders { get; set; } = [];
    public bool PomodoroFocus { get; set; } = true;
    public string RestoreToasts { get; set; } = ""; // "1"/"0" while our "Не беспокоить" is on

    // behaviour
    public bool HideInFullscreen { get; set; } = true;
    /// <summary>Attached to the top edge of the screen like a notch (true) or floating pill (false).</summary>
    public bool Docked { get; set; }
    public bool ExpandOnHover { get; set; } = true;

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

    // chat
    public string ChatProvider { get; set; } = "gemini";

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DynamicIsland", "settings.json");

    private const int CurrentVersion = 3;

    private static Settings Load()
    {
        Settings s;
        try
        {
            s = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings()
                : new Settings();
        }
        catch { s = new Settings(); } // corrupted file: start fresh

        if (s.Version < CurrentVersion)
        {
            // v3: glass was unreadable and the waves too much → solid & calm
            s.GlassOpacity = 1;
            s.LiquidWaves = false;
            s.Version = CurrentVersion;
        }
        return s;
    }

    /// <summary>Demo / screenshot mode: never touch the user's files.</summary>
    public static bool ReadOnly { get; set; }

    public void Save()
    {
        if (ReadOnly) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            // write to a temp file and swap: a kill in the middle can't leave a half-written settings file
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch { /* not critical */ }
    }
}

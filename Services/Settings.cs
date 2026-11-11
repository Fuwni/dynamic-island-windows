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
    /// <summary>1 = solid (readable). Kept for old settings files.</summary>
    public double GlassOpacity { get; set; } = 1;
    /// <summary>Jelly / ripple physics (off = calm, smooth animation).</summary>
    public bool LiquidWaves { get; set; }
    public int Version { get; set; }

    // live activities & extras
    public bool GameStats { get; set; }
    public bool Karaoke { get; set; } = true;
    public bool MirrorNotifications { get; set; } = true;
    public bool MirrorAllApps { get; set; }
    public string CalendarIcs { get; set; } = "";
    public List<Reminder> Reminders { get; set; } = [];
    public bool PomodoroFocus { get; set; } = true;
    public string RestoreToasts { get; set; } = ""; // "1"/"0" while our "Не беспокоить" is on

    // behaviour
    public bool HideInFullscreen { get; set; } = true;
    public bool ExpandOnHover { get; set; } = true;

    // Home tab, "build it yourself"
    public bool HomeActions { get; set; } = true;
    public bool HomeMusic { get; set; } = true;
    public bool HomeWeather { get; set; } = true;
    public List<string> Actions { get; set; } =
        ["region", "full", "ocr", "qr", "picker", "cleantext", "record", "command"];

    // actions
    public string Command { get; set; } = "";
    public string TranslateTo { get; set; } = "ru";
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
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* not critical */ }
    }
}

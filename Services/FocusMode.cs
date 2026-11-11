using Microsoft.Win32;

namespace DynamicIsland.Services;

/// <summary>
/// "Do not disturb": switches Windows notification banners off for a while
/// (the same switch as Settings → System → Notifications) and always puts the old value back.
/// </summary>
public static class FocusMode
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
    private const string Value = "NOC_GLOBAL_SETTING_TOASTS_ENABLED";

    public static DateTime? Until { get; private set; }
    public static bool IsOn => Until.HasValue && Until > DateTime.Now;

    public static void Start(TimeSpan duration)
    {
        var s = Settings.Current;
        if (string.IsNullOrEmpty(s.RestoreToasts))
        {
            // remember what the user had (missing value = enabled)
            using var k = Registry.CurrentUser.OpenSubKey(Key);
            s.RestoreToasts = k?.GetValue(Value) is int v ? v.ToString() : "1";
            s.Save();
        }
        Set(0);
        Until = DateTime.Now + duration;
    }

    public static void End()
    {
        Until = null;
        var s = Settings.Current;
        if (string.IsNullOrEmpty(s.RestoreToasts)) return;
        Set(s.RestoreToasts == "0" ? 0 : 1);
        s.RestoreToasts = "";
        s.Save();
    }

    /// <summary>After a crash the old value is still in settings - put it back on start.</summary>
    public static void RecoverAfterCrash()
    {
        if (!string.IsNullOrEmpty(Settings.Current.RestoreToasts)) End();
    }

    public static void Tick()
    {
        if (Until.HasValue && Until <= DateTime.Now) End();
    }

    private static void Set(int enabled)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(Key);
            k.SetValue(Value, enabled, RegistryValueKind.DWord);
        }
        catch { }
    }
}

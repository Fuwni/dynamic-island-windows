using Microsoft.Win32;

namespace DynamicIsland.Services;

/// <summary>
/// Start with Windows via HKCU\...\Run (per user, no admin rights needed), or, in the Store version,
/// through the package's startup task (Settings → Apps → Startup lists it).
/// </summary>
public static class Autostart
{
    private const string TaskId = "AtollStartup";

    private static Windows.ApplicationModel.StartupTask? Task_() =>
        Task.Run(async () => await Windows.ApplicationModel.StartupTask.GetAsync(TaskId)).GetAwaiter().GetResult();

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DynamicIsland";

    private static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled
    {
        get
        {
            if (AppPackage.IsPackaged)
            {
                try { return Task_()?.State is Windows.ApplicationModel.StartupTaskState.Enabled or Windows.ApplicationModel.StartupTaskState.EnabledByPolicy; }
                catch { return false; }
            }
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value &&
                   string.Equals(value, Command, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Set(bool enabled)
    {
        if (AppPackage.IsPackaged)
        {
            var task = Task_();
            if (task == null) return;
            if (enabled) Task.Run(async () => await task.RequestEnableAsync()).GetAwaiter().GetResult();
            else task.Disable();
            return;
        }
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, Command);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}

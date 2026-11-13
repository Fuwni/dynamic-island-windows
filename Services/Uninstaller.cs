using System.Diagnostics;
using System.IO;

namespace DynamicIsland.Services;

/// <summary>
/// Removing the app cleanly: its Claude Code hooks, the autostart entry and (if asked) settings, notes and chat logins.
/// Installed copies use the installer's own uninstaller; a portable folder removes itself after the app exits.
/// </summary>
public static class Uninstaller
{
    public static string AppDataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland");
    public static string LocalDataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland");

    /// <summary>The installer's uninstaller next to the exe, when the app was installed with the setup.</summary>
    public static string? InstalledUninstaller =>
        Directory.EnumerateFiles(AppContext.BaseDirectory, "unins*.exe").FirstOrDefault();

    /// <summary>Running from a source checkout (bin\Release): never delete that folder.</summary>
    public static bool IsDevBuild
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
                if (dir.EnumerateFiles("*.csproj").Any()) return true;
            return false;
        }
    }

    /// <summary>Undo everything the app registered outside its folder (safe to run more than once).</summary>
    public static void RemoveRegistrations()
    {
        try { if (Settings.Current.CleanDesktop) CleanDesktop.Apply(false, Settings.Current.TaskbarWasAutoHide, Settings.Current.IconsWereVisible); } catch { }
        try { if (ClaudeHooks.IsInstalled) ClaudeHooks.Uninstall(); } catch (Exception e) { Log.Write("uninstall hooks: " + e.Message); }
        // only our own autostart entry: another copy of the island may own it
        try { if (Autostart.IsEnabled) Autostart.Set(false); } catch (Exception e) { Log.Write("uninstall autostart: " + e.Message); }
    }

    /// <summary>
    /// After this process exits, delete the data folders and/or the portable app folder.
    /// Done by a tiny hidden cmd that waits for files to be released.
    /// </summary>
    public static void ScheduleDelete(bool data, bool appFolder)
    {
        var targets = new List<string>();
        if (data) { targets.Add(AppDataFolder); targets.Add(LocalDataFolder); }
        if (appFolder && !IsDevBuild) targets.Add(AppContext.BaseDirectory.TrimEnd('\\'));
        if (targets.Count == 0) return;
        var script = "timeout /t 3 /nobreak >nul & " + string.Join(" & ", targets.Select(t => $"rmdir /s /q \"{t}\""));
        Process.Start(new ProcessStartInfo("cmd.exe", "/c " + script)
        {
            CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath(),
        });
    }
}

using System.IO;
using Microsoft.Win32;

namespace DynamicIsland.Services;

/// <summary>
/// Which apps use the microphone / camera right now. Windows records it in
/// CapabilityAccessManager\ConsentStore (LastUsedTimeStop == 0 while in use) - the same data the
/// taskbar mic icon uses.
/// </summary>
public static class PrivacyMonitor
{
    public sealed record Usage(IReadOnlyList<string> Mic, IReadOnlyList<string> Camera);

    public static Usage Read() => new(InUse("microphone"), InUse("webcam"));

    private static List<string> InUse(string capability)
    {
        var apps = new List<string>();
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(
                $@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{capability}");
            if (root == null) return apps;

            foreach (var name in root.GetSubKeyNames())
            {
                if (name == "NonPackaged")
                {
                    using var np = root.OpenSubKey(name);
                    if (np == null) continue;
                    foreach (var exe in np.GetSubKeyNames())
                    {
                        using var k = np.OpenSubKey(exe);
                        if (!Active(k)) continue;
                        // Windows sometimes never closes the record (app crashed / updated mid-call):
                        // trust it only if that exact program still exists and is running
                        var path = exe.Replace('#', '\\');
                        var exeName = Path.GetFileNameWithoutExtension(path);
                        if (!File.Exists(path) || !IsRunning(path, exeName)) continue;
                        apps.Add(exeName);
                    }
                }
                else
                {
                    using var k = root.OpenSubKey(name);
                    if (Active(k)) apps.Add(PackagedName(name));
                }
            }
        }
        catch { }
        return apps.Where(a => !a.Equals("DynamicIsland", StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
    }

    private static bool Active(RegistryKey? k)
    {
        if (k == null) return false;
        var start = k.GetValue("LastUsedTimeStart") is long s ? s : 0;
        var stop = k.GetValue("LastUsedTimeStop") is long e ? e : 0;
        if (start <= 0 || stop != 0) return false;
        // a "still recording" mark older than a day is a leftover, not a live session
        try { return DateTime.Now - DateTime.FromFileTime(start) < TimeSpan.FromHours(24); }
        catch { return false; }
    }

    private static bool IsRunning(string path, string name)
    {
        foreach (var p in System.Diagnostics.Process.GetProcessesByName(name))
        {
            try
            {
                if (string.Equals(p.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { return true; } // can't read the path (elevated process) - name match is enough
            finally { p.Dispose(); }
        }
        return false;
    }

    private static string PackagedName(string family)
    {
        // "Microsoft.WindowsCamera_8wekyb3d8bbwe" -> "WindowsCamera"
        var n = family.Split('_')[0];
        var dot = n.LastIndexOf('.');
        return dot >= 0 ? n[(dot + 1)..] : n;
    }
}

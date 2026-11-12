using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland.Services;

/// <summary>Installed programs for the command bar: every shortcut in the Start menu.</summary>
public sealed class AppIndex
{
    public sealed record App(string Name, string Path);

    private List<App> _apps = [];
    private readonly Dictionary<string, ImageSource?> _icons = new();
    private Task? _loading;

    public Task EnsureLoadedAsync() => _loading ??= Task.Run(Load);

    private void Load()
    {
        var found = new Dictionary<string, App>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                     Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                     Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                 })
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    var name = System.IO.Path.GetFileNameWithoutExtension(f);
                    if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("удал", StringComparison.OrdinalIgnoreCase)) continue;
                    found.TryAdd(name, new App(name, f));
                }
            }
            catch { }
        }
        _apps = found.Values.OrderBy(a => a.Name).ToList();
    }

    /// <summary>Best matches first: starts-with, then word-starts-with, then contains.</summary>
    public List<App> Search(string q, int max = 5)
    {
        q = q.Trim();
        if (q.Length == 0) return [];
        return _apps
            .Select(a => (App: a, Score: Score(a.Name, q)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenBy(x => x.App.Name.Length)
            .Take(max).Select(x => x.App).ToList();
    }

    private static int Score(string name, string q)
    {
        if (name.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return 3;
        if (name.Split(' ', '-', '_').Any(w => w.StartsWith(q, StringComparison.OrdinalIgnoreCase))) return 2;
        return name.Contains(q, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    /// <summary>The program's own icon (cached). Call on the UI thread.</summary>
    public ImageSource? Icon(string path)
    {
        if (_icons.TryGetValue(path, out var cached)) return cached;
        ImageSource? img = null;
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon != null)
            {
                img = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                img.Freeze();
            }
        }
        catch { }
        return _icons[path] = img;
    }

    public static void Launch(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
    }
}

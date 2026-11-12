using System.IO;
using System.Runtime.InteropServices;

namespace DynamicIsland.Services;

/// <summary>
/// Watches the Downloads folder for files that browsers are still writing
/// (.crdownload, .part, .download, .partial, .opdownload) and reports speed + finished files.
/// </summary>
public sealed class DownloadWatcher
{
    public sealed record Active(string Name, long Bytes, double BytesPerSecond, int Count);

    private static readonly string[] TempExt = [".crdownload", ".part", ".download", ".partial", ".opdownload", ".tmp"];
    private readonly Dictionary<string, (long Size, DateTime At, double Speed)> _seen = new();
    private readonly string _folder = DownloadsFolder();
    private DateTime _lastActive;

    public Active? Current { get; private set; }

    /// <summary>Full path of a download that just finished.</summary>
    public event Action<string>? Completed;

    public void Poll()
    {
        if (!Directory.Exists(_folder)) return;
        var now = DateTime.Now;
        List<FileInfo> temps;
        try
        {
            temps = new DirectoryInfo(_folder).EnumerateFiles()
                .Where(f => TempExt.Contains(f.Extension.ToLowerInvariant()) && now - f.LastWriteTime < TimeSpan.FromMinutes(2))
                .ToList();
        }
        catch { return; }

        foreach (var f in temps)
        {
            long size = f.Length;
            if (_seen.TryGetValue(f.FullName, out var prev))
            {
                double secs = Math.Max(0.2, (now - prev.At).TotalSeconds);
                double speed = Math.Max(0, (size - prev.Size) / secs);
                _seen[f.FullName] = (size, now, prev.Speed * 0.6 + speed * 0.4);
            }
            else _seen[f.FullName] = (size, now, 0);
        }

        // temp files that vanished = finished (or cancelled)
        var gone = _seen.Keys.Where(k => temps.All(t => t.FullName != k)).ToList();
        foreach (var g in gone)
        {
            _seen.Remove(g);
            var finished = FindFinished(g);
            if (finished != null) Completed?.Invoke(finished);
        }

        if (temps.Count > 0)
        {
            _lastActive = now;
            var main = temps.OrderByDescending(t => _seen[t.FullName].Speed).First();
            var s = _seen[main.FullName];
            Current = new Active(Pretty(main.Name), s.Size, s.Speed, temps.Count);
        }
        else if (now - _lastActive > TimeSpan.FromSeconds(2)) Current = null;
    }

    private string? FindFinished(string tempPath)
    {
        try
        {
            // Chrome/Edge: "name.ext.crdownload" -> "name.ext"; Firefox: "name.ext.part"
            var direct = Path.Combine(Path.GetDirectoryName(tempPath)!, Path.GetFileNameWithoutExtension(tempPath));
            if (File.Exists(direct)) return direct;
            // Chrome "Unconfirmed 123.crdownload": take the newest real file written just now
            return new DirectoryInfo(_folder).EnumerateFiles()
                .Where(f => !TempExt.Contains(f.Extension.ToLowerInvariant()) && DateTime.Now - f.LastWriteTime < TimeSpan.FromSeconds(8))
                .OrderByDescending(f => f.LastWriteTime)
                .FirstOrDefault()?.FullName;
        }
        catch { return null; }
    }

    private static string Pretty(string tempName)
    {
        var n = Path.GetFileNameWithoutExtension(tempName);
        return n.StartsWith("Unconfirmed", StringComparison.OrdinalIgnoreCase) || n.StartsWith("Неподтвержден", StringComparison.OrdinalIgnoreCase)
            ? L.T("файл") : n;
    }

    public static string FormatBytes(double b) =>
        b >= 1 << 30 ? L.F("{0:0.0} ГБ", b / (1 << 30)) : b >= 1 << 20 ? L.F("{0:0.0} МБ", b / (1 << 20)) : L.F("{0:0} КБ", Math.Max(1, b / 1024));

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

    public static string DownloadsFolder()
    {
        try
        {
            if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out var p) == 0)
            {
                var s = Marshal.PtrToStringUni(p)!;
                Marshal.FreeCoTaskMem(p);
                return s;
            }
        }
        catch { }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }
}

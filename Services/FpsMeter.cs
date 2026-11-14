using System.Diagnostics;
using System.IO;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace DynamicIsland.Services;

/// <summary>
/// Frames per second of the game in front, measured like PresentMon: every frame a game shows calls
/// Present, and Windows reports it as an event (DXGI for DirectX 10/11/12 and most Vulkan games, D3D9 for
/// older ones). We count those events per process. Reading them needs administrator rights or membership
/// in the built-in "Performance Log Users" group (<see cref="GrantAccess"/> adds the user once).
/// </summary>
public sealed class FpsMeter : IDisposable
{
    private static readonly Guid Dxgi = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3D9 = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private readonly string _sessionName;

    public FpsMeter(string sessionName = "AtollFps") => _sessionName = sessionName;

    private TraceEventSession? _session;
    private readonly Dictionary<int, Queue<DateTime>> _presents = [];
    private readonly object _lock = new();

    public bool Running => _session != null;
    /// <summary>True when Windows refused: the user needs to grant access once.</summary>
    public bool AccessDenied { get; private set; }

    public bool Start()
    {
        if (_session != null) return true;
        try
        {
            var session = new TraceEventSession(_sessionName) { StopOnDispose = true };
            session.EnableProvider(Dxgi, TraceEventLevel.Informational);
            session.EnableProvider(D3D9, TraceEventLevel.Informational);
            session.Source.AllEvents += OnEvent;
            _session = session;
            AccessDenied = false;
            new Thread(() =>
            {
                try { session.Source.Process(); }
                catch (Exception e) { Log.Write("fps: " + e.Message); }
            }) { IsBackground = true, Name = "FPS" }.Start();
            return true;
        }
        catch (UnauthorizedAccessException) { AccessDenied = true; }
        catch (Exception e) { Log.Write("fps start: " + e.Message); AccessDenied = e.HResult == unchecked((int)0x80070005); }
        _session = null;
        return false;
    }

    public void Stop()
    {
        var s = _session;
        _session = null;
        try { s?.Dispose(); } catch { }
        lock (_lock) _presents.Clear();
    }

    private void OnEvent(TraceEvent e)
    {
        // DXGI Present_Start = 42, PresentMultiplaneOverlay_Start = 55; D3D9 Present_Start = 1
        bool present = (e.ProviderGuid == Dxgi && (int)e.ID is 42 or 55) || (e.ProviderGuid == D3D9 && (int)e.ID == 1);
        if (!present) return;
        var t = e.TimeStamp;
        lock (_lock)
        {
            if (!_presents.TryGetValue(e.ProcessID, out var q)) _presents[e.ProcessID] = q = new Queue<DateTime>();
            q.Enqueue(t);
            while (q.Count > 0 && (t - q.Peek()).TotalSeconds > 1) q.Dequeue();
        }
    }

    /// <summary>Frames in the last second for this process, or null when it isn't presenting.</summary>
    public int? FpsOf(int pid)
    {
        lock (_lock)
        {
            if (!_presents.TryGetValue(pid, out var q) || q.Count == 0) return null;
            // events arrive in batches about once a second: judge by the newest one
            var newest = q.Last();
            if ((DateTime.Now - newest).TotalSeconds > 3) return null;
            return q.Count(t => (newest - t).TotalSeconds <= 1);
        }
    }

    /// <summary>FPS of every process presenting right now (for the helper).</summary>
    public Dictionary<int, int> All()
    {
        var pids = new List<int>();
        lock (_lock) pids.AddRange(_presents.Keys);
        var d = new Dictionary<int, int>();
        foreach (var pid in pids) if (FpsOf(pid) is int f) d[pid] = f;
        return d;
    }

    // ------------------------------------------------------------------ the helper: FPS right away, no sign-out

    /// <summary>Where the elevated helper writes {pid: fps} twice a second.</summary>
    public static string HelperFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", "fps-helper.json");

    /// <summary>
    /// Started as administrator once (the same UAC prompt as granting access): adds the user to
    /// "Performance Log Users" for the next sign-in, and meanwhile measures FPS itself and hands the numbers
    /// to Atoll through a small file — so FPS works at once, without signing out. Ends with Atoll.
    /// </summary>
    public static void RunHelper(int parentPid)
    {
        Log.Write($"fps helper: started for {parentPid}");
        try { AddToGroup(); } catch (Exception e) { Log.Write("fps helper: group: " + e.Message); }
        using var meter = new FpsMeter("AtollFpsHelper");
        if (!meter.Start()) { Log.Write($"fps helper: no access (denied={meter.AccessDenied})"); return; }
        Process? parent;
        try { parent = Process.GetProcessById(parentPid); } catch { Log.Write("fps helper: Atoll is gone"); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(HelperFile)!);
        while (!parent.HasExited)
        {
            try
            {
                var json = System.Text.Json.JsonSerializer.Serialize(meter.All().ToDictionary(k => k.Key.ToString(), k => k.Value));
                var tmp = HelperFile + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, HelperFile, true);
            }
            catch { }
            Thread.Sleep(500);
        }
        try { File.Delete(HelperFile); } catch { }
    }

    /// <summary>FPS of a process from the helper's file, when the helper is running.</summary>
    public static int? FromHelper(int pid)
    {
        try
        {
            var f = new FileInfo(HelperFile);
            if (!f.Exists || (DateTime.UtcNow - f.LastWriteTimeUtc).TotalSeconds > 3) return null;
            var d = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(HelperFile));
            return d != null && d.TryGetValue(pid.ToString(), out var v) ? v : null;
        }
        catch { return null; }
    }

    public static bool HelperAlive()
    {
        try { var f = new FileInfo(HelperFile); return f.Exists && (DateTime.UtcNow - f.LastWriteTimeUtc).TotalSeconds <= 3; }
        catch { return false; }
    }

    /// <summary>One UAC prompt: start the helper as administrator.</summary>
    public static bool StartHelper()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--fps-helper {Environment.ProcessId}")
            {
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden,
            });
            return p != null;
        }
        catch { return false; }   // the user said no
    }

    private static void AddToGroup()
    {
        var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
        var script = $"Add-LocalGroupMember -SID 'S-1-5-32-559' -Member '{user.Replace("'", "''")}' -ErrorAction SilentlyContinue";
        using var p = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -WindowStyle Hidden -Command \"{script}\"")
        { UseShellExecute = false, CreateNoWindow = true });
        p?.WaitForExit(30000);
    }

    /// <summary>
    /// Ask Windows (one UAC prompt) to add this user to "Performance Log Users" (S-1-5-32-559),
    /// which allows reading the frame events without running anything as administrator.
    /// Takes effect after signing out and back in.
    /// </summary>
    public static bool GrantAccess()
    {
        try
        {
            var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
            var script = $"Add-LocalGroupMember -SID 'S-1-5-32-559' -Member '{user.Replace("'", "''")}' -ErrorAction SilentlyContinue";
            using var p = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -WindowStyle Hidden -Command \"{script}\"")
            {
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden,
            });
            p?.WaitForExit(30000);
            return p?.ExitCode == 0;
        }
        catch { return false; }   // the user said no to the prompt
    }

    public void Dispose() => Stop();
}

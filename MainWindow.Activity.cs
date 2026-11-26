using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Media;
using DynamicIsland.Services;
using Forms = System.Windows.Forms;

namespace DynamicIsland;

/// <summary>
/// Things the island notices by itself: long jobs of other programs, a messenger blinking in the taskbar
/// (when Windows keeps no notifications to read), and which monitor the mouse is on.
/// </summary>
public partial class MainWindow
{
    private readonly ProgressWatcher _progress = new();

    // ------------------------------------------------------------------ a messenger blinks in the taskbar

    [DllImport("user32.dll")] private static extern bool RegisterShellHookWindow(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);

    private int _shellHookMsg;
    private readonly Dictionary<string, DateTime> _flashAt = [];

    private static readonly string[] MessengerProcesses =
        ["telegram", "ayugram", "kotatogram", "64gram", "discord", "viber", "whatsapp", "signal", "slack", "teams", "skype", "vkmessenger", "element", "zoom", "max"];

    /// <summary>Windows tells every "shell hook" window when a taskbar button starts to blink.</summary>
    private void InitShellHook()
    {
        if (IsToolRun || _hwnd == IntPtr.Zero) return;
        try
        {
            if (RegisterShellHookWindow(_hwnd)) _shellHookMsg = (int)RegisterWindowMessage("SHELLHOOK");
        }
        catch { _shellHookMsg = 0; }
    }

    /// <summary>
    /// The usual way (reading Windows notifications) gives the text of a message. Where it can't work — the
    /// notification center is switched off, or access was refused — a messenger that blinks in the taskbar
    /// still means "a new message": the island says so, without the text.
    /// </summary>
    private bool FlashFallback => !Settings.Safe && _settings.MirrorNotifications && (!_notifications.Available || NotificationMirror.CenterDisabled());

    private void OnWindowFlash(IntPtr window)
    {
        if (!FlashFallback || _demoMedia != null || window == IntPtr.Zero || window == _hwnd) return;
        if (window == Native.GetForegroundWindow()) return;   // you are looking at it
        string name;
        try
        {
            Native.GetWindowThreadProcessId(window, out uint pid);
            using var p = Process.GetProcessById((int)pid);
            name = p.ProcessName;
        }
        catch { return; }
        bool messenger = MessengerProcesses.Any(m => name.Contains(m, StringComparison.OrdinalIgnoreCase));
        if (!messenger && !_settings.MirrorAllApps) return;
        NewMessageNotice(name, window);
    }

    // ------------------------------------------------------------------ the unread counter in a messenger's title

    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);

    private readonly Dictionary<IntPtr, int> _titleUnread = [];
    private readonly Dictionary<uint, string> _titleNames = [];
    private readonly System.Text.StringBuilder _titleBuf = new(300);
    private string? _titleTestProcess;   // tests: a process to treat as a messenger
    private int _titlePolls;

    /// <summary>"Chat – (3)" of Telegram, "(3) Discord | …", "(2) WhatsApp": the number in brackets at either end.</summary>
    private static int UnreadInTitle(string title)
    {
        var m = System.Text.RegularExpressions.Regex.Match(title, @"^\s*\((\d{1,7})\)|\((\d{1,7})\)\s*$");
        if (!m.Success) return 0;
        return int.Parse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
    }

    /// <summary>
    /// Every two seconds, only while Windows gives no notifications to read: messengers write the number of
    /// unread messages into their window title. When it grows, a message came. Only that number is used.
    /// </summary>
    private void WatchMessengerTitles()
    {
        if (!FlashFallback || _demoMedia != null) return;
        if (++_titlePolls % 150 == 0) _titleNames.Clear();   // process ids get reused
        var fg = Native.GetForegroundWindow();
        var seen = new List<(IntPtr Window, string App, int Count)>();
        EnumWindows((h, _) =>
        {
            Native.GetWindowThreadProcessId(h, out uint pid);
            if (!_titleNames.TryGetValue(pid, out var name))
            {
                try { using var p = Process.GetProcessById((int)pid); name = p.ProcessName; } catch { name = ""; }
                bool messenger = MessengerProcesses.Any(m => name.Contains(m, StringComparison.OrdinalIgnoreCase)) || name.Equals(_titleTestProcess, StringComparison.OrdinalIgnoreCase);
                _titleNames[pid] = name = messenger ? name : "";
            }
            if (name.Length == 0) return true;
            _titleBuf.Clear();
            Native.GetWindowText(h, _titleBuf, _titleBuf.Capacity);
            if (_titleBuf.Length == 0) return true;
            seen.Add((h, name, UnreadInTitle(_titleBuf.ToString())));
            return true;
        }, IntPtr.Zero);

        foreach (var (window, app, count) in seen)
        {
            bool known = _titleUnread.TryGetValue(window, out int was);
            _titleUnread[window] = count;
            // only "there was nothing unread, now there is": a counter that is never empty (hundreds of muted
            // channels) ticks all day and must not turn into a notice every minute
            if (known && was == 0 && count > 0 && window != fg) NewMessageNotice(app, window);
        }
        if (_titleUnread.Count > 64)
            foreach (var gone in _titleUnread.Keys.Where(k => seen.All(s => s.Window != k)).ToList()) _titleUnread.Remove(gone);
    }

    private void NewMessageNotice(string name, IntPtr window)
    {
        var now = DateTime.Now;
        if (_flashAt.TryGetValue(name, out var at) && now - at < TimeSpan.FromSeconds(8)) return;   // one blink series = one notice
        _flashAt[name] = now;

        _unread++;
        Log.Write("taskbar blink: " + name);
        if (FocusMode.IsOn) return;
        var app = ProgressWatcher.Friendly(name);
        WhenNotTyping(() => ShowToast("", app, L.T("Новое сообщение"), () => Native.SetForegroundWindow(window),
            new SolidColorBrush(Color.FromRgb(0x2A, 0xAB, 0xEE)), ms: 4500, keep: true));
    }

    // ------------------------------------------------------------------ the monitor the mouse is on

    private string _otherMonitor = "";
    private DateTime _otherMonitorSince;

    /// <summary>Once a second: with "follow the mouse" on, the folded island hops to the monitor you work on.</summary>
    private void WatchMonitor()
    {
        if (!_settings.FollowMonitor || _dragging || _hidden || _fullscreenHidden || _mode != Mode.Compact || _liveDemo) return;
        if (Forms.Screen.AllScreens.Length < 2) return;
        double dpi = Dpi;
        var cur = Forms.Screen.FromPoint(Forms.Cursor.Position);
        var c = IslandCenter();
        var mine = Forms.Screen.FromPoint(new System.Drawing.Point((int)(c.X * dpi), (int)(c.Y * dpi)));
        if (cur.DeviceName == mine.DeviceName) { _otherMonitor = ""; return; }
        var now = DateTime.Now;
        if (_otherMonitor != cur.DeviceName) { _otherMonitor = cur.DeviceName; _otherMonitorSince = now; return; }
        if (now - _otherMonitorSince < TimeSpan.FromSeconds(1.2)) return;   // just passing through
        _otherMonitor = "";

        // the same spot on the other screen: as far from its left edge (as a share of its width), as far from its top
        double sx = (c.X * dpi - mine.Bounds.Left) / Math.Max(1, mine.Bounds.Width), sy = (c.Y * dpi - mine.Bounds.Top) / Math.Max(1, mine.Bounds.Height);
        var at = new System.Windows.Point((cur.Bounds.Left + sx * cur.Bounds.Width) / dpi, (cur.Bounds.Top + sy * cur.Bounds.Height) / dpi);
        if (_settings.Attach != "") _settings.AttachAt = [at.X, at.Y];   // the same edge there, as far along it
        else
        {
            double top = (cur.Bounds.Top + Math.Clamp(Top * dpi - mine.Bounds.Top, 0, Math.Max(0, cur.Bounds.Height - 160))) / dpi;
            if (_placeOwner != null) _settings.AppPlaces[_placeOwner] = [at.X, top];
            else { _settings.CenterX = at.X; _settings.Top = top; }
        }
        _settings.Save();
        ClampPosition();
        // it drops in out of its edge, like after being hidden
        _y.Snap(-AwayDistance(30));
        _y.Target = 0;
        Kick(3);
        StartRendering();
    }
}

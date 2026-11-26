using System.Diagnostics;
using System.Windows;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// Staying out of the way of other programs.
/// Yield: a cursor flying onto the folded island over another window (a game's mouse, a hand on its way to the
/// tabs) finds it nearly transparent and its clicks go through to the window under it; resting on it, or the
/// very top edge, brings it back (<see cref="CursorYield"/>). In a full-screen game it stays out of the way.
/// A place for each program: moved while a program is in front, the island remembers that place and floats
/// there whenever that program comes to the front, and back to its usual place for the others.
/// </summary>
public partial class MainWindow
{
    private const int WS_EX_TRANSPARENT = 0x20;
    private readonly CursorYield _yield = new();
    private readonly Stopwatch _yieldClock = Stopwatch.StartNew();
    private bool _fullscreenFront;
    private bool? _testWindowUnder;   // tests: pretend another program's window is (or isn't) under the island
    private bool _testFrontApps;      // tests: the front program comes from the test, not from Windows

    // the program in front (process name, lower case; null: the desktop or nothing) and whose place the island is at
    private string? _frontApp;
    private string? _placeOwner;
    private uint _frontPid;
    private string? _frontPidName;

    /// <summary>~30 times a second, from the gaze loop.</summary>
    private void UpdateYield(Native.POINT cursor)
    {
        if (!_settings.YieldToCursor || _mode != Mode.Compact || _dragging || _hidden || _fullscreenHidden || !Island.IsVisible)
        {
            if (_yield.Reset()) StopYield(reopen: false);
            return;
        }
        Rect island;
        try
        {
            var tl = Island.PointToScreen(new Point(0, 0));
            // both corners from the screen: the island's own size setting (80–130 %) counts too
            island = new Rect(tl, Island.PointToScreen(new Point(Island.ActualWidth, Island.ActualHeight)));
            island.Inflate(4, 4);
        }
        catch (InvalidOperationException) { return; }
        var p = new Point(cursor.X, cursor.Y);
        bool inside = island.Contains(p);
        bool top = inside && p.Y <= System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(cursor.X, cursor.Y)).Bounds.Top + 1;
        bool under = inside && (_testWindowUnder ?? WindowUnder(island));
        int change = _yield.Step(_yieldClock.ElapsedMilliseconds, p, inside, top, under, _gameMode || _fullscreenFront);
        if (change > 0) StartYield();
        else if (change < 0) StopYield(reopen: inside);
    }

    /// <summary>Another program's window (not the desktop, not the taskbar, not ours) lies under the island.</summary>
    private bool WindowUnder(Rect island)
    {
        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == _hwnd || OwnsWindow(fg) || Native.IsIconic(fg)) return false;
        if (Native.ClassName(fg) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        if (!Native.GetWindowRect(fg, out var r)) return false;
        return new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top).IntersectsWith(island);
    }

    private void StartYield()
    {
        _expandDelay.Stop();
        Release();
        Unsquish(0);
        ClickThrough(true);
        Fade(Root, 0.12, 150, 0);
    }

    private void StopYield(bool reopen)
    {
        ClickThrough(false);
        if (!_fullscreenHidden) Fade(Root, 1, 180, 0);
        // the cursor rests on it: carry on as if it had just come over it (it may open on hover)
        if (reopen) Island_MouseEnter(Island, null!);
    }

    private void ClickThrough(bool on)
    {
        if (_hwnd == IntPtr.Zero) return;
        int ex = Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE, on ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT);
    }

    /// <summary>The program in front, by process name (lower case); null for the desktop, the taskbar, a minimized window.</summary>
    private string? AppOf(IntPtr fg)
    {
        if (fg == IntPtr.Zero || Native.IsIconic(fg)) return null;
        if (Native.ClassName(fg) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return null;
        Native.GetWindowThreadProcessId(fg, out uint pid);
        if (pid == 0) return null;
        if (pid != _frontPid)
        {
            _frontPid = pid;
            try { using var proc = Process.GetProcessById((int)pid); _frontPidName = proc.ProcessName.ToLowerInvariant(); }
            catch (Exception e) { _frontPidName = null; Log.Write("place per app: no name for pid " + pid + ": " + e.Message); }
        }
        return _frontPidName;
    }

    /// <summary>From the 4-a-second watch: the front program changed? Float to its place (or back to the usual one).</summary>
    private void WatchFrontApp(IntPtr fg)
    {
        if (_testFrontApps || fg == _hwnd || (fg != IntPtr.Zero && OwnsWindow(fg))) return;   // our own window in front: the program before it still counts
        OnFrontApp(AppOf(fg));
    }

    private void OnFrontApp(string? app)
    {
        if (app == _frontApp) return;
        _frontApp = app;
        if (!_settings.PlacePerApp || _settings.Attach != "" || _dragging || _hidden || _editMode) return;
        string? slot = app != null && _settings.AppPlaces.ContainsKey(app) ? app : null;
        if (slot == _placeOwner) return;
        _placeOwner = slot;
        FloatToPlace();
    }

    /// <summary>A drag begins: the place it ends at belongs to the program in front (or is the usual one).</summary>
    private void OnDragStarted() => _placeOwner = _settings.PlacePerApp && _settings.Attach == "" ? _frontApp : null;

    private (double Cx, double Top) PlaceOf(string? slot) =>
        slot != null && _settings.AppPlaces.TryGetValue(slot, out var at) && at.Length == 2
            ? (at[0], at[1])
            : (_settings.CenterX ?? SystemParameters.PrimaryScreenWidth / 2, _settings.Top ?? 0);

    private void FloatToPlace()
    {
        UpdateEdge();
        FollowEdge(animate: true);
    }

    /// <summary>"Align to centre" while a program has its own place: forget that place, back to the usual one.</summary>
    private bool ForgetAppPlace()
    {
        if (_placeOwner == null) return false;
        _settings.AppPlaces.Remove(_placeOwner);
        _placeOwner = null;
        _settings.Save();
        FloatToPlace();
        Kick(3);
        return true;
    }
}

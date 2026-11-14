using System.Runtime.InteropServices;
using System.Windows;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// Game mode extras (FPS) and folding the open island away when you click somewhere else.
/// </summary>
public partial class MainWindow
{
    private readonly FpsMeter _fps = new();

    private void OnGameModeChanged(bool game)
    {
        if (game && !IsToolRun) _fps.Start();
        else _fps.Stop();
        UpdateAuraVisibility();
    }

    /// <summary>The text of the game pill: FPS of the game in front (when we may read it) and the load.</summary>
    private int? _demoFps;

    private string GameStatsText(SystemStats.Snapshot s)
    {
        if (_liveDemo && _demoFps is int df)
        {
            var r = Random.Shared;
            return $"{df + r.Next(-4, 4)} FPS · CPU {38 + r.Next(-4, 5)}% · GPU {88 + r.Next(-3, 4)}%";
        }
        int? fps = null;
        var fg = Native.GetForegroundWindow();
        if (fg != IntPtr.Zero && Native.GetWindowThreadProcessId(fg, out var pid) != 0)
            fps = _fps.Running ? _fps.FpsOf((int)pid) : FpsMeter.FromHelper((int)pid);
        return fps is int f
            ? $"{f} FPS · CPU {s.Cpu:0}% · GPU {s.Gpu:0}%"
            : $"CPU {s.Cpu:0}% · GPU {s.Gpu:0}% · RAM {s.Ram:0}%";
    }

    // ------------------------------------------------------------------ settings: FPS access

    private void RefreshFpsAccess()
    {
        if (IsToolRun) return;
        bool ok = _fps.Running || FpsMeter.HelperAlive() || ProbeFps();
        FpsStatus.Text = ok
            ? L.T("Счётчик FPS работает: в играх он появится в пилюле")
            : L.T("Чтобы видеть FPS в играх, нужно один раз разрешить доступ");
        FpsGrantButton.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
    }

    private static bool ProbeFps()
    {
        using var probe = new FpsMeter();
        bool ok = probe.Start();
        probe.Stop();
        return ok;
    }

    private void FpsGrant_Click(object sender, RoutedEventArgs e)
    {
        // one "Yes" in the Windows prompt: FPS works right away (a helper measures it until Atoll closes),
        // and from the next sign-in Atoll reads it by itself, without the helper
        if (!FpsMeter.StartHelper())
        {
            ShowToast("\uE7BA", L.T("Доступ не выдан"), L.T("Windows попросит подтверждение — нажмите «Да»"));
            return;
        }
        FpsStatus.Text = L.T("Включаю счётчик FPS…");
        FpsGrantButton.Visibility = Visibility.Collapsed;
        Dispatcher.InvokeAsync(async () =>
        {
            for (int i = 0; i < 20 && !FpsMeter.HelperAlive(); i++) await Task.Delay(500);
            if (FpsMeter.HelperAlive()) ShowToast("\uE7FC", L.T("FPS включён"), L.T("Выходить из Windows не нужно"));
            RefreshFpsAccess();
        });
    }

    // ------------------------------------------------------------------ click outside to close

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Native.POINT p);

    private bool _mouseWasDown;

    /// <summary>~30 times a second: a click anywhere outside the open island folds it back.</summary>
    private void CheckClickOutside(Native.POINT cursor)
    {
        bool down = (GetAsyncKeyState(0x01) & 0x8000) != 0 || (GetAsyncKeyState(0x02) & 0x8000) != 0;
        bool pressed = down && !_mouseWasDown;
        _mouseWasDown = down;
        if (!pressed || _mode != Mode.Expanded || _dragging || _fakeCursor != null || !IsLoaded) return;

        Point tl, br;
        try
        {
            tl = Island.PointToScreen(new Point(0, 0));
            br = Island.PointToScreen(new Point(Island.ActualWidth, Island.ActualHeight));
        }
        catch { return; }
        if (cursor.X >= tl.X && cursor.X <= br.X && cursor.Y >= tl.Y && cursor.Y <= br.Y) return;

        // our own pop-ups (menus, dialogs, the screenshot editor) don't count as "elsewhere"
        var under = WindowFromPoint(cursor);
        if (under != IntPtr.Zero && Native.GetWindowThreadProcessId(under, out var pid) != 0 && pid == Environment.ProcessId) return;

        if (AppSearchPanel.Visibility == Visibility.Visible) _ = ToggleAppSearch(false);
        _pinnedOpen = false;
        SetMode(Mode.Compact);
    }
}

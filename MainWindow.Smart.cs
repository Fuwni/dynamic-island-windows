using System.Runtime.InteropServices;
using System.Windows.Controls;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// The smart island (Settings › Behavior): it watches what you do and fits in.
///  • opens on what matters right now — a running timer, a Discord call, something you just copied, the
///    music — or on the tab you usually open at this time of day (it learns this on its own, on this PC);
///  • while you type it doesn't pop open under a passing cursor, and messenger notices wait for a pause;
///  • on a laptop running low on battery it switches to light graphics by itself.
/// Nothing leaves the PC: the habits are counts per tab and hour in the settings file.
/// </summary>
public partial class MainWindow
{
    private bool Smart => _settings.SmartIsland;

    // ------------------------------------------------------------------ open on the right tab

    private DateTime _closedAt = DateTime.MinValue, _copiedAt = DateTime.MinValue;

    /// <summary>The user opens the folded island (hover or click): pick the tab, then open.</summary>
    private void OpenSmart()
    {
        if (Smart && SmartTab() is { } tab && tab.IsChecked != true) tab.IsChecked = true;
        Expand();
    }

    /// <summary>Which tab fits this moment best, or null to keep the current one.</summary>
    private RadioButton? SmartTab()
    {
        // came back within a few seconds: they want what they just left
        if ((DateTime.Now - _closedAt).TotalSeconds < 20) return null;

        var score = new Dictionary<RadioButton, double>();
        void Add(RadioButton t, double s) => score[t] = score.GetValueOrDefault(t) + s;

        // what's going on right now
        if (_timer.Active) Add(TabTimer, _timer.Running ? 3.2 : 2.2);
        if (_discordInCall) Add(TabSystem, 2.4);
        if ((DateTime.Now - _copiedAt).TotalSeconds < 25) Add(TabClip, 2.6);
        if (_lastMedia?.IsPlaying == true) Add(TabMusic, 1.4);

        // habits: the tabs opened most at this hour (and the hours around it)
        int h = DateTime.Now.Hour;
        double total = 0;
        var habit = new Dictionary<string, double>();
        foreach (var (name, hours) in _settings.TabHabits)
        {
            if (hours.Length != 24) continue;
            double v = hours[h] + 0.5 * (hours[(h + 23) % 24] + hours[(h + 1) % 24]);
            habit[name] = v;
            total += v;
        }
        if (total >= 6)   // enough to go on
            foreach (var (name, v) in habit)
                if (PageTabs.FirstOrDefault(t => t.Name == name) is { } tab) Add(tab, 2.0 * v / total);

        if (score.Count == 0) return null;
        var best = score.MaxBy(kv => kv.Value);
        return best.Value >= 0.6 ? best.Key : null;
    }

    // ------------------------------------------------------------------ learning the habits

    private readonly Dictionary<string, double> _openSecs = [];

    /// <summary>Every second (from SlowTick): time spent on each tab while open; when it folds, remember the main one.</summary>
    private void TrackHabits()
    {
        if (_mode == Mode.Expanded && !_liveDemo && _demoMedia == null)
        {
            var tab = PageTabs.FirstOrDefault(t => t.IsChecked == true);
            if (tab != null) _openSecs[tab.Name] = _openSecs.GetValueOrDefault(tab.Name) + 1;
            return;
        }
        if (_openSecs.Count == 0) return;
        _closedAt = DateTime.Now;
        var main = _openSecs.MaxBy(kv => kv.Value);
        _openSecs.Clear();
        if (main.Value < 3 || !Smart) return;   // a glance, not a use
        if (!_settings.TabHabits.TryGetValue(main.Key, out var hours) || hours.Length != 24)
            _settings.TabHabits[main.Key] = hours = new int[24];
        hours[DateTime.Now.Hour] = Math.Min(hours[DateTime.Now.Hour] + 1, 500);
        _settings.Save();
    }

    private void ResetHabits_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _settings.TabHabits.Clear();
        _settings.Save();
        ShowToast("", L.T("Привычки забыты"), L.T("Остров начнёт учиться заново"));
    }

    // ------------------------------------------------------------------ typing

    [StructLayout(LayoutKind.Sequential)] private struct LastInput { public uint Size, Time; }
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput li);

    private uint _lastInputSeen;
    private Native.POINT _lastCursorSeen;
    private DateTime _typedAt = DateTime.MinValue;
    private readonly List<Action> _deferred = [];

    /// <summary>Typing right now: input came in while the mouse stood still (the keyboard, not the mouse).</summary>
    private bool Typing => Smart && (DateTime.Now - _typedAt).TotalSeconds < 2.0;

    /// <summary>Every 250 ms: notice typing; when it pauses, show what waited.</summary>
    private void WatchTyping()
    {
        var li = new LastInput { Size = 8 };
        Native.GetCursorPos(out var c);
        if (GetLastInputInfo(ref li) && li.Time != _lastInputSeen)
        {
            bool mouseStill = c.X == _lastCursorSeen.X && c.Y == _lastCursorSeen.Y;
            if (mouseStill && _lastInputSeen != 0) _typedAt = DateTime.Now;
            _lastInputSeen = li.Time;
        }
        _lastCursorSeen = c;
        if (_deferred.Count > 0 && !Typing)
        {
            var later = _deferred.ToList();
            _deferred.Clear();
            foreach (var a in later) a();
        }
    }

    /// <summary>A notice that can wait until you stop typing (messages); urgent ones show at once.</summary>
    private void WhenNotTyping(Action show)
    {
        if (Typing) { if (_deferred.Count < 10) _deferred.Add(show); }
        else show();
    }

    // ------------------------------------------------------------------ battery

    private bool _batterySaver;

    /// <summary>Every 30 s: on battery with 20 % or less, light graphics turn on by themselves (and off when charging).</summary>
    private void WatchBattery()
    {
        bool saver = false;
        if (Smart && ControlCenter.Battery() is { } b) saver = !b.Charging && b.Percent <= 20;
        if (saver == _batterySaver) return;
        _batterySaver = saver;
        Log.Write("battery saver " + (saver ? "on: light graphics" : "off"));
        ApplyGlass(); UpdateAuraVisibility(); ApplyScene();
        if (saver) ShowToast("", L.T("Мало заряда — облегчённая графика"), L.T("Эффекты вернутся, когда подключите зарядку"));
    }
}

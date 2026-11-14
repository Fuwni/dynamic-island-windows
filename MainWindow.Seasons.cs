using System.Windows;
using System.Windows.Threading;

namespace DynamicIsland;

/// <summary>The animated season themes: runs the scene inside the island while it can be seen.</summary>
public partial class MainWindow
{
    private readonly DispatcherTimer _seasonTimer = new(DispatcherPriority.Render);
    private DateTime _seasonLast;

    /// <summary>Show the scene of the current theme (or none), and start / stop its animation.</summary>
    private void ApplyScene()
    {
        var kind = _settings.Scene ?? "";
        if (Array.IndexOf(Views.SeasonView.Kinds, kind) < 0) kind = "";
        Season.Kind = kind;
        Season.Visibility = kind.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_seasonTimer.Interval == TimeSpan.Zero)
        {
            _seasonTimer.Interval = TimeSpan.FromMilliseconds(33);
            _seasonTimer.Tick += (_, _) => SeasonTick();
        }
        _seasonLast = DateTime.UtcNow;
        if (kind.Length > 0) _seasonTimer.Start(); else _seasonTimer.Stop();
        SeasonTick();
    }

    private void SeasonTick()
    {
        if (Season.Kind.Length == 0) { _seasonTimer.Stop(); return; }
        var now = DateTime.UtcNow;
        double dt = (now - _seasonLast).TotalSeconds;
        _seasonLast = now;
        bool folded = _mode is Mode.Compact or Mode.Toast or Mode.Claude;
        Season.Compact = folded;
        // nobody sees it: hidden, over a game or full-screen app — don't spend anything on it
        if (_hidden || _fullscreenHidden || _gameMode || !IsVisible) return;
        if (Lite)
        {
            // light graphics: a still picture of the season, drawn once
            Season.InvalidateVisual();
            return;
        }
        // the folded pill is tiny: half the frame rate is plenty there
        _seasonTimer.Interval = TimeSpan.FromMilliseconds(folded ? 66 : 33);
        Season.Step(dt);
    }
}

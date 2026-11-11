using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// Live activities in the compact pill (timer, focus, Discord call, downloads, next event, game stats, karaoke),
/// mic / camera dots, messenger notifications, and the System tab.
/// </summary>
public partial class MainWindow
{
    private readonly TimerService _timer = new();
    private readonly DownloadWatcher _downloads = new();
    private readonly CalendarService _calendar = new();
    private readonly SystemStats _stats = new();
    private readonly NotificationMirror _notifications = new();

    private PrivacyMonitor.Usage _privacy = new([], []);
    private bool _discordInCall, _discordSpeaking;
    private CalendarService.Event? _soonEvent;
    private readonly HashSet<string> _eventNotified = [];
    private int _unread;
    private int _slowCount;
    private bool _focusByPomodoro;
    private bool _statsBusy;

    private void InitExtras()
    {
        FocusMode.RecoverAfterCrash();
        _timer.Finished += (title, text) =>
        {
            PlayChime();
            if (_timer.Kind == TimerKind.Pomodoro && _settings.PomodoroFocus)
            {
                if (_timer.PomodoroWork) { FocusMode.Start(TimerService.Work); _focusByPomodoro = true; }
                else if (_focusByPomodoro) { FocusMode.End(); _focusByPomodoro = false; }
            }
            ShowToast("", title, text, () => OpenTab(TabTimer), (Brush)FindResource("Accent"), ms: 6000);
            RenderTimer();
        };
        _downloads.Completed += path => ShowToast("", "Загрузка завершена", Path.GetFileName(path),
            () => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }),
            (Brush)FindResource("AccentBlue"), ms: 5000);

        InitTimerPage();
        IcsBox.Text = _settings.CalendarIcs;
        PomodoroFocusSwitch.IsChecked = _settings.PomodoroFocus;
        _ = _calendar.RefreshAsync(force: true);
        _ = InitNotificationsAsync();
    }

    private async Task InitNotificationsAsync()
    {
        if (!await _notifications.InitAsync()) Log.Write("notification listener: access not allowed");
        await _notifications.PollAsync(_settings.MirrorAllApps); // remember what's already there
    }

    // ================================================================ every 250 ms

    private void UpdateLive()
    {
        _timer.Tick();
        UpdateEyesVisibility();

        string? icon = null, text = null;
        double ring = -1;
        Brush ringBrush = (Brush)FindResource("Accent");

        if (_recording.IsRecording)
        {
            // handled by OnTick (red dot + elapsed)
        }
        else if (_timer.Active)
        {
            icon = _timer.Kind == TimerKind.Stopwatch ? "" : _timer.Kind == TimerKind.Pomodoro ? "" : "";
            text = _timer.Display;
            ring = _timer.Progress;
            if (_timer.Kind == TimerKind.Pomodoro && !_timer.PomodoroWork) ringBrush = (Brush)FindResource("AccentBlue");
            if (!_timer.Running) text += " · пауза";
        }
        else if (FocusMode.IsOn)
        {
            icon = "";
            var left = FocusMode.Until!.Value - DateTime.Now;
            text = $"Не беспокоить · {(int)left.TotalMinutes}:{left.Seconds:00}";
            ringBrush = new SolidColorBrush(Color.FromRgb(0x5E, 0x5C, 0xE6));
        }
        else if (_discordInCall)
        {
            icon = "";
            text = _audio.MicMuted == true ? "Discord · микрофон выкл" : _discordSpeaking ? "Discord · говорят" : "Discord · в канале";
            ring = _discordSpeaking ? 1 : 0;
            ringBrush = new SolidColorBrush(Color.FromRgb(0x58, 0x65, 0xF2));
        }
        else if (_downloads.Current is { } d)
        {
            icon = "";
            text = $"{Shorten(d.Name, 22)} · {DownloadWatcher.FormatBytes(d.BytesPerSecond)}/с" + (d.Count > 1 ? $" · ещё {d.Count - 1}" : "");
            ring = (_tickCount % 8) / 8.0; // spinner: size of the file is unknown
            ringBrush = (Brush)FindResource("AccentBlue");
        }
        else if (_soonEvent is { } ev)
        {
            icon = "";
            var mins = (int)Math.Ceiling((ev.Start - DateTime.Now).TotalMinutes);
            text = mins <= 0 ? $"Сейчас: {Shorten(ev.Title, 26)}" : $"{Shorten(ev.Title, 22)} · через {mins} мин";
        }
        else if (_gameMode && _stats.Last is { } s)
        {
            icon = "";
            text = $"CPU {s.Cpu:0}% · GPU {s.Gpu:0}% · RAM {s.Ram:0}%";
        }
        else if (_settings.Karaoke && _lastMedia?.IsPlaying == true && _currentLyrics?.Synced == true &&
                 LyricCur.Text is { Length: > 0 } line && line != "♪")
        {
            text = Shorten(line, 44); // lyrics right in the pill
        }

        LiveSlot.Visibility = icon != null ? Visibility.Visible : Visibility.Collapsed;
        if (icon != null)
        {
            LiveIcon.Text = icon;
            LiveRing.Stroke = ringBrush;
            LiveRing.Data = ring > 0 ? RingGeometry(22, 2.5, ring) : null;
            Eyes.Visibility = MiniArt.Visibility = Visibility.Collapsed;
        }
        if (text != null)
        {
            CompactTime.Text = text;
            CompactTime.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Fg");
        }

        // right side: privacy dots + unread badge
        MicDot.Visibility = _privacy.Mic.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        CamDot.Visibility = _privacy.Camera.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UnreadBadge.Visibility = _unread > 0 ? Visibility.Visible : Visibility.Collapsed;
        UnreadText.Text = _unread > 99 ? "99+" : _unread.ToString();
        if (text != null && text.Length > 18) CompactWeather.Visibility = Visibility.Collapsed; // keep the pill compact

        if (_mode == Mode.Compact && !_hidden)
        {
            double w = CompactWidth;
            if (Math.Abs(w - _w.Target) > 3) MorphTo(new Size(w, CompactHeight));
        }

        if (_mode == Mode.Expanded && TabTimer.IsChecked == true) UpdateTimerPage();
    }

    private static string Shorten(string s, int max) => s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";

    /// <summary>Arc from 12 o'clock, clockwise.</summary>
    private static Geometry RingGeometry(double size, double thickness, double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 0.9999);
        double r = (size - thickness) / 2, c = size / 2;
        double a = fraction * 2 * Math.PI;
        var start = new Point(c, c - r);
        var end = new Point(c + r * Math.Sin(a), c - r * Math.Cos(a));
        var fig = new PathFigure { StartPoint = start, IsClosed = false };
        fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        var g = new PathGeometry([fig]);
        g.Freeze();
        return g;
    }

    // ================================================================ every second

    private async void SlowTick()
    {
        _slowCount++;
        FocusMode.Tick();
        _downloads.Poll();
        _privacy = _demoMedia != null ? new([], []) : PrivacyMonitor.Read(); // demo shows nothing real

        // forgotten paused media disappears after a while
        if (_lastMedia != null && PausedTooLong) UpdateMediaUi();

        // Discord call: Discord has a live microphone stream open (not just an old Windows record)
        _discordInCall = Process.GetProcessesByName("Discord").Length > 0 &&
                         _audio.AppsRecording().Any(a => a.Contains("discord", StringComparison.OrdinalIgnoreCase));
        _discordSpeaking = _discordInCall && _audio.AppState("discord").Peak > 0.03;

        CheckCalendar();

        if (_slowCount % 2 == 0 && _settings.MirrorNotifications) await PollNotificationsAsync();
        if (_slowCount % 900 == 0) _ = _calendar.RefreshAsync();

        bool systemVisible = _mode == Mode.Expanded && TabSystem.IsChecked == true;
        if ((_gameMode || systemVisible) && !_statsBusy)
        {
            _statsBusy = true;
            await Task.Run(_stats.Sample);
            _statsBusy = false;
            if (systemVisible) await RefreshSystemAsync(full: false);
        }
    }

    private async Task PollNotificationsAsync()
    {
        var messages = await _notifications.PollAsync(_settings.MirrorAllApps);
        foreach (var m in messages)
        {
            _unread++;
            if (FocusMode.IsOn) continue; // quiet, only the counter grows
            Brush? badge = m.Logo != null ? new ImageBrush(m.Logo) { Stretch = Stretch.UniformToFill } : null;
            ShowToast(m.Logo != null ? "" : "", m.Title, string.IsNullOrEmpty(m.Body) ? m.App : m.Body,
                () => FocusApp(m.App), badge, ms: 5000);
        }
    }

    private void ClearUnread() => _unread = 0;

    private static void FocusApp(string app)
    {
        var token = app.Split(' ')[0];
        var p = Process.GetProcesses().FirstOrDefault(x =>
            x.MainWindowHandle != IntPtr.Zero && x.ProcessName.Contains(token, StringComparison.OrdinalIgnoreCase));
        if (p != null) Native.SetForegroundWindow(p.MainWindowHandle);
    }

    private void CheckCalendar()
    {
        var now = DateTime.Now;
        var upcoming = _calendar.Upcoming(1).Where(e => !e.AllDay).ToList();
        _soonEvent = upcoming.FirstOrDefault(e => e.Start - now <= TimeSpan.FromMinutes(15) && e.Start - now > TimeSpan.FromMinutes(-5));

        foreach (var e in upcoming)
        {
            var key = e.Title + e.Start.Ticks;
            var left = e.Start - now;
            if (!e.IsReminder && left <= TimeSpan.FromMinutes(10) && left > TimeSpan.Zero && _eventNotified.Add(key + "10"))
                ShowToast("", $"Через {(int)Math.Ceiling(left.TotalMinutes)} мин", e.Title, () => OpenTab(TabCalendar), ms: 6000);
            if (left <= TimeSpan.Zero && left > TimeSpan.FromMinutes(-2) && _eventNotified.Add(key + "0"))
            {
                PlayChime();
                ShowToast(e.IsReminder ? "" : "", e.IsReminder ? "Напоминание" : "Начинается", e.Title,
                    () => OpenTab(TabCalendar), (Brush)FindResource("Warm"), ms: 8000);
            }
        }

        // old reminders clean themselves up
        int before = _settings.Reminders.Count;
        _settings.Reminders.RemoveAll(r => r.When < now.AddHours(-12));
        if (_settings.Reminders.Count != before) _settings.Save();
    }

    private static void PlayChime()
    {
        try
        {
            var wav = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Windows Notify Calendar.wav");
            if (File.Exists(wav)) new System.Media.SoundPlayer(wav).Play();
            else System.Media.SystemSounds.Asterisk.Play();
        }
        catch { }
    }

    // ================================================================ System tab

    private DateTime _batteryAt;

    private async Task RefreshSystemAsync(bool full)
    {
        if (_stats.Last == null && !_statsBusy)
        {
            // first sample can take seconds (perf counters warm up) - don't hold the tab for it
            _statsBusy = true;
            _ = Task.Run(_stats.Sample).ContinueWith(_ => _statsBusy = false);
        }
        // README screenshots: calm made-up numbers instead of the real machine
        var stats = _demoMedia != null ? new SystemStats.Snapshot(23, 41, 54, 8.6, 16, "GeForce RTX 4060") : _stats.Last;
        if (stats is { } s)
        {
            CpuText.Text = $"{s.Cpu:0}%";
            CpuBar.ScaleX = s.Cpu / 100;
            GpuText.Text = $"{s.Gpu:0}%";
            GpuBar.ScaleX = s.Gpu / 100;
            GpuCaption.Text = string.IsNullOrEmpty(s.GpuName) ? "ВИДЕОКАРТА" : s.GpuName.ToUpperInvariant();
            RamText.Text = $"{s.RamUsedGb:0.0} / {s.RamTotalGb:0} ГБ";
            RamBar.ScaleX = s.Ram / 100;
        }

        DiscordStatus.Text = _discordInCall ? (_discordSpeaking ? "В канале · кто-то говорит" : "В голосовом канале") : "Не в голосовом канале";
        DiscordBadge.Opacity = _discordInCall ? 1 : 0.5;
        var mic = _audio.MicMuted;
        MicMuteButton.Content = mic == true ? "Микрофон: выкл" : "Микрофон: вкл";
        var discord = _audio.AppState("discord");
        DiscordMuteButton.Content = !discord.Found ? "Discord не запущен" : discord.Muted ? "Звук Discord: выкл" : "Звук Discord: вкл";
        DiscordMuteButton.IsEnabled = discord.Found;

        FocusStatus.Text = FocusMode.IsOn
            ? $"До {FocusMode.Until:HH:mm} · баннеры Windows скрыты"
            : "Выключено";

        var parts = new List<string>();
        if (_privacy.Mic.Count > 0) parts.Add("🎙 " + string.Join(", ", _privacy.Mic));
        if (_privacy.Camera.Count > 0) parts.Add("📷 " + string.Join(", ", _privacy.Camera));
        PrivacyText.Text = parts.Count > 0 ? string.Join("\n", parts) : "Никто не использует";

        if (full && DateTime.Now - _batteryAt > TimeSpan.FromSeconds(30))
        {
            _batteryAt = DateTime.Now;
            BatteryText.Text = "Проверяю…";
            var check = Task.Run(async () =>
            {
                var list = await HeadsetBattery.ReadFromGHubAsync();
                if (list.Count == 0 && HeadsetBattery.Read() is { } r) list.Add(r);
                return list;
            });
            // never leave "Проверяю…" hanging
            var readings = await Task.WhenAny(check, Task.Delay(6000)) == check ? check.Result : [];
            bool g435 = await Task.Run(() => HeadsetBattery.HasDevice(0x0ACB));
            BatteryText.Text = readings.Count > 0
                ? string.Join("\n", readings.Select(x => $"{x.Device}: {x.Percent}%{(x.Charging ? " ⚡" : "")}"))
                : g435
                    ? "Logitech G435 не передаёт заряд компьютеру — даже G HUB его не показывает. Здесь появятся устройства, которые умеют сообщать заряд."
                    : "Нет устройств, которые сообщают заряд.";
            if (_demoMedia != null) BatteryText.Text = "Нет устройств, которые сообщают заряд.";
            Remeasure();
        }
    }

    private void MicMute_Click(object sender, RoutedEventArgs e)
    {
        _audio.MicMuted = !(_audio.MicMuted ?? false);
        ShowToastIfCompact(_audio.MicMuted == true ? "" : "", _audio.MicMuted == true ? "Микрофон выключен" : "Микрофон включён");
        _ = RefreshSystemAsync(false);
    }

    private void DiscordMute_Click(object sender, RoutedEventArgs e)
    {
        _audio.ToggleAppMute("discord");
        _ = RefreshSystemAsync(false);
    }

    private void Focus_Click(object sender, RoutedEventArgs e)
    {
        int minutes = int.Parse((string)((FrameworkElement)sender).Tag);
        if (minutes == 0) FocusMode.End();
        else FocusMode.Start(TimeSpan.FromMinutes(minutes));
        _focusByPomodoro = false;
        _ = RefreshSystemAsync(false);
    }

    private void ShowToastIfCompact(string glyph, string title)
    {
        if (_mode != Mode.Expanded) ShowToast(glyph, title, null);
    }
}

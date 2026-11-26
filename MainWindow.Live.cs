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
    private bool _downloadsBusy, _callProbeBusy, _progressBusy;
    private int _callMisses;
    private readonly AudioService _audioProbe = new();   // used on background threads only
    private bool _statsBusy;

    private void InitExtras()
    {
        FocusMode.RecoverAfterCrash();
        TimerService.Work = TimeSpan.FromMinutes(Math.Clamp(_settings.PomodoroWorkMin, 5, 90));
        TimerService.ShortBreak = TimeSpan.FromMinutes(Math.Clamp(_settings.PomodoroBreakMin, 5, 30));
        TimerService.LongBreak = TimeSpan.FromMinutes(Math.Clamp(_settings.PomodoroLongMin, 10, 45));
        _timer.WorkDone += () =>
        {
            (_settings.PomodoroCountedOn, _settings.PomodoroDone) = PomodoroCount.Add(_settings.PomodoroCountedOn, _settings.PomodoroDone, DateTime.Now);
            if (!Settings.ReadOnly) _settings.Save();
        };
        _timer.Finished += (title, text) =>
        {
            PlayChime();
            if (_timer.Kind == TimerKind.Pomodoro && _settings.PomodoroFocus)
            {
                if (_timer.PomodoroWork) { FocusMode.Start(TimerService.Work); _focusByPomodoro = true; }
                else if (_focusByPomodoro) { FocusMode.End(); _focusByPomodoro = false; }
            }
            ShowToast("", title, text, () => OpenTab(TabTimer), (Brush)FindResource("Accent"), ms: 6000, keep: true);
            RenderTimer();
        };
        _downloads.Completed += path => Dispatcher.BeginInvoke(() => ShowToast("", L.T("Загрузка завершена"), Path.GetFileName(path),
            () => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }),
            (Brush)FindResource("AccentBlue"), ms: 5000, keep: true));

        _progress.Finished += app =>
        {
            if (_settings.ShowProgress && !FocusMode.IsOn)
                ShowToast("\uE73E", L.T("Готово"), ProgressWatcher.Friendly(app), null, (Brush)FindResource("Accent"), ms: 4000, keep: true);
        };
        Safely("shell hook", InitShellHook);
        Safely("check page", InitSelfCheck);
        Safely("widgets", InitWidgets);
        Safely("welcome", InitWelcome);

        Safely("timer page", InitTimerPage);
        IcsBox.Text = _settings.CalendarIcs;
        PomodoroFocusSwitch.IsChecked = _settings.PomodoroFocus;
        _ = _calendar.RefreshAsync(force: true);
        _ = InitNotificationsAsync();
    }

    private async Task InitNotificationsAsync()
    {
        if (!await _notifications.InitAsync()) Log.Write("notification listener: access not allowed");
        if (NotificationMirror.CenterDisabled()) Log.Write("notification listener: the notification center is disabled by policy (DisableNotificationCenter): nothing to read");
        await _notifications.PollAsync(_settings.MirrorAllApps); // remember what's already there
    }

    // ================================================================ every 250 ms

    private void UpdateLive()
    {
        WatchTyping();
        UpdateIdleDim();
        _timer.Tick();
        UpdateEyesVisibility();

        string? icon = null, text = null;
        double ring = -1;
        Brush ringBrush = (Brush)FindResource("Accent");

        // everything that wants the pill right now, most important first; the mouse wheel over the
        // pill switches between them (a Discord call and the music, a timer and a download…)
        var lives = new List<(string Id, string? Icon, string? Text, double Ring, Brush Brush)>();
        if (!_recording.IsRecording)   // recording owns the pill (red dot + elapsed, see OnTick)
        {
            if (_gameMode && _stats.Last is { } gs)
            {
                if (_gameText == null || _tickCount % 4 == 0) _gameText = GameStatsText(gs);   // once a second: every change redraws the window over the game
                lives.Add(("game", "\uE7FC", _gameText, -1, ringBrush));
            }
            if (_timer.Active)
            {
                var b = _timer.Kind == TimerKind.Pomodoro && !_timer.PomodoroWork ? (Brush)FindResource("AccentBlue") : ringBrush;
                lives.Add(("timer", _timer.Kind == TimerKind.Stopwatch ? "\uE916" : _timer.Kind == TimerKind.Pomodoro ? "\uE7C1" : "\uE823",
                           _timer.Display + (_timer.Running ? "" : L.T(" · пауза")), _timer.Progress, b));
            }
            if (FocusMode.IsOn)
            {
                var left = FocusMode.Until!.Value - DateTime.Now;
                lives.Add(("focus", "\uE708", L.F("Не беспокоить · {0}:{1:00}", (int)left.TotalMinutes, left.Seconds), -1,
                           new SolidColorBrush(Color.FromRgb(0x5E, 0x5C, 0xE6))));
            }
            if (_discordInCall && _settings.ShowDiscordCall && !DiscordBeside)
                lives.Add(("discord", "\uE7F6",
                           _audio.MicMuted == true ? L.T("Discord · микрофон выкл") : _discordSpeaking ? L.T("Discord · говорят") : L.T("Discord · в канале"),
                           _discordSpeaking ? 1 : 0, new SolidColorBrush(Color.FromRgb(0x58, 0x65, 0xF2))));
            if (_downloads.Current is { } d)
                lives.Add(("download", "\uE896",
                           L.F("{0} · {1}/с", Shorten(d.Name, 22), DownloadWatcher.FormatBytes(d.BytesPerSecond)) + (d.Count > 1 ? L.F(" · ещё {0}", d.Count - 1) : ""),
                           (_tickCount % 8) / 8.0, (Brush)FindResource("AccentBlue")));
            if (_settings.ShowProgress && _progress.Current is { } job)
                lives.Add(("progress", job.App.Equals("explorer", StringComparison.OrdinalIgnoreCase) ? "\uE8C8" : "\uE895",
                           $"{ProgressWatcher.Friendly(job.App)} · {job.Percent}%", Math.Max(0.02, job.Percent / 100.0), (Brush)FindResource("AccentBlue")));
            if (_soonEvent is { } ev)
            {
                var mins = (int)Math.Ceiling((ev.Start - DateTime.Now).TotalMinutes);
                lives.Add(("event", "\uE787", mins <= 0 ? L.F("Сейчас: {0}", Shorten(ev.Title, 26)) : L.F("{0} · через {1} мин", Shorten(ev.Title, 22), mins), -1, ringBrush));
            }
            if (_lastMedia?.IsPlaying == true)
            {
                string? karaoke = _settings.Karaoke && !NoKaraokeHere && _currentLyrics?.Synced == true && LyricCur.Text is { Length: > 0 } line && line != "♪" ? line : null;   // the whole line: the island grows to fit it
                lives.Add(("music", null, karaoke, -1, ringBrush));   // the cover, the equaliser and the eyes
            }
        }
        var pick = lives.FirstOrDefault(l => l.Id == _liveChoice);
        if (pick.Id == null) { pick = lives.FirstOrDefault(); if (_liveChoice != null && lives.All(l => l.Id != _liveChoice)) _liveChoice = null; }
        _liveIds = lives.Select(l => l.Id).ToList();
        if (pick.Id != null) { icon = pick.Icon; text = pick.Text; ring = pick.Ring; ringBrush = pick.Brush; }
        UpdateLiveDots(pick.Id);
        UpdateDiscordPill();

        LiveSlot.Visibility = icon != null ? Visibility.Visible : Visibility.Collapsed;
        if (icon != null)
        {
            LiveIcon.Text = icon;
            LiveRing.Stroke = ringBrush;
            LiveRing.Data = ring > 0 ? RingGeometry(22, 2.5, ring) : null;
            Eyes.Visibility = MiniArt.Visibility = Visibility.Collapsed;
        }
        // karaoke has its own view (light running over the words, lines springing in); anything else is plain text
        bool kara = pick.Id == "music" && text != null && _mode == Mode.Compact;
        if (kara != (Karaoke.Visibility == Visibility.Visible))
        {
            Karaoke.Visibility = kara ? Visibility.Visible : Visibility.Collapsed;
            CompactTime.Visibility = kara ? Visibility.Collapsed : Visibility.Visible;
            if (!kara) MorphSprings(grow: true);   // back to the island's usual width spring
        }
        if (kara) Karaoke.Tint = _albumColor;
        else if (text != null)
        {
            CompactTime.Text = text;
            CompactTime.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "Fg");
        }

        // right side: privacy dots + unread badge
        MicDot.Visibility = _privacy.Mic.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        CamDot.Visibility = _privacy.Camera.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UnreadBadge.Visibility = _unread > 0 ? Visibility.Visible : Visibility.Collapsed;
        UnreadText.Text = _unread > 99 ? "99+" : _unread.ToString();
        if (text != null && text.Length > 18)   // keep the pill compact
        {
            CompactWeather.Visibility = Visibility.Collapsed;
            if (_pillBattery != null) _pillBattery.Visibility = Visibility.Collapsed;
        }

        if (_mode == Mode.Compact && !_hidden)
        {
            var want = PillSize(CompactWidth);
            double len = Vertical ? want.Height : want.Width, now = Vertical ? _h.Target : _w.Target;
            // the digits of the game pill change all the time: it grows for them, but doesn't shrink for every one
            if (len > now + 3 || len < now - (pick.Id == "game" ? 24 : 3) || Math.Abs((Vertical ? _w.Target : _h.Target) - CompactHeight) > 1) MorphTo(want);
        }

        if (_mode == Mode.Expanded && TabTimer.IsChecked == true) UpdateTimerPage();
    }

    private string? _liveChoice;            // the activity the user switched to with the wheel
    private List<string> _liveIds = [];

    /// <summary>Wheel over the folded pill: next activity (or the previous one).</summary>
    private void SwitchLive(int step)
    {
        if (_liveIds.Count < 2) return;
        int i = _liveChoice == null ? 0 : Math.Max(0, _liveIds.IndexOf(_liveChoice));
        _liveChoice = _liveIds[((i + step) % _liveIds.Count + _liveIds.Count) % _liveIds.Count];
        Squish(1.04);
        Kick(1.5);
        UpdateLive();
    }

    /// <summary>Small dots under the pill's content: how many activities there are and which one is shown.</summary>
    private void UpdateLiveDots(string? current)
    {
        LiveDots.Children.Clear();
        LiveDots.Visibility = _liveIds.Count > 1 && _mode == Mode.Compact && !Vertical ? Visibility.Visible : Visibility.Collapsed;
        if (_liveIds.Count < 2) return;
        foreach (var id in _liveIds)
        {
            var dot = new System.Windows.Shapes.Ellipse { Width = id == current ? 5 : 3.5, Height = id == current ? 5 : 3.5, Margin = new Thickness(1.5, 0, 1.5, 0), VerticalAlignment = VerticalAlignment.Center };
            dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, id == current ? "Fg" : "Fg3");
            LiveDots.Children.Add(dot);
        }
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

    private async Task SlowTick()
    {
        CheckAlarms();
        UpdateWidgetPause();
        Measure("frost", CaptureFrost);   // the glass's grab loop picks up whatever changed (hidden, light graphics, a setting)
        _slowCount++;
        FocusMode.Tick();
        UiStep("slow tick");
        // the Downloads folder can sit on a sleeping or network drive: never wait for it on the UI thread
        if (!_downloadsBusy)
        {
            _downloadsBusy = true;
            _ = Task.Run(() => { try { _downloads.Poll(); } catch (Exception ex) { Log.Error("downloads: poll", ex); } }).ContinueWith(_ => _downloadsBusy = false);
        }
        if (_settings.ShowProgress && !_gameMode && _demoMedia == null && !_progressBusy)
        {
            _progressBusy = true;
            _ = Task.Run(_progress.Scan).ContinueWith(t =>
            {
                _progressBusy = false;
                if (t.IsFaulted) { Log.Write("progress: " + t.Exception?.InnerException?.Message); return; }
                Measure("progress watcher", () => _progress.Apply(t.Result));
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        if (_slowCount % 4 == 1) Measure("volume device", WatchVolumeDevice);
        WatchMonitor();
        if (_slowCount % 2 == 1) WatchMessengerTitles();
        if (_slowCount % 2 == 0 || _demoMedia != null)
            _privacy = _demoMedia != null ? new([], []) : PrivacyMonitor.Read(); // demo shows nothing real

        // forgotten paused media disappears after a while
        if (_lastMedia != null && PausedTooLong) UpdateMediaUi();

        // Discord call: Discord has a live microphone stream open (not just an old Windows record)
        // listing every process is not free: look for Discord every 3 s
        // asked on a background thread (Windows audio can take its time, and the island must not freeze for it)
        if ((_slowCount % 3 == 0 || _discordInCall) && !_callProbeBusy && !_liveDemo)
        {
            _callProbeBusy = true;
            _ = Task.Run(() =>
            {
                bool inCall = false, speaking = false;
                try
                {
                    inCall = _audioProbe.AppsRecording().Any(a => a.Contains("discord", StringComparison.OrdinalIgnoreCase)) &&
                             Running("Discord");
                    speaking = inCall && _audioProbe.AppState("discord").Peak > 0.03;
                }
                catch (Exception ex) { Log.Error("discord: call probe", ex); }
                return (inCall, speaking);
            }).ContinueWith(t =>
            {
                _callProbeBusy = false;
                if (_liveDemo || t.IsFaulted) return;
                var (inCall, speaking) = t.Result;
                // one missed reading is not the end of a call (the microphone stream blinks when devices switch)
                if (inCall) { _discordInCall = true; _callMisses = 0; }
                else if (++_callMisses >= 2) _discordInCall = false;
                _discordSpeaking = _discordInCall && speaking;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        if (_liveDemo) _discordInCall = _discordSpeaking = _demoDiscord;

        CheckCalendar();

        if (_slowCount % 2 == 0 && _settings.MirrorNotifications) await Measure("notifications", PollNotificationsAsync);
        if (_slowCount % 900 == 0) _ = _calendar.RefreshAsync();
        TrackHabits();
        if (_slowCount % 30 == 1) WatchBattery();
        if (_slowCount % 30 == 0 && _settings.CaptureHidden == null)   // NVIDIA's recorder may start later
        {
            bool was = _excluded == true;
            ApplyCaptureExclusion();
            if (was != (_excluded == true)) { CaptureFrost(); UpdateGlassFill(); }
        }

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
        // read on the thread pool: Windows can take its time, the island must not wait for it
        bool all = _settings.MirrorAllApps;
        var messages = await Task.Run(() => _notifications.PollAsync(all));
        foreach (var m in messages)
        {
            _unread++;
            if (FocusMode.IsOn) continue; // quiet, only the counter grows
            Brush? badge = m.Logo != null ? new ImageBrush(m.Logo) { Stretch = Stretch.UniformToFill } : null;
            var msg = m;
            // the smart island holds messages while you type and shows them at the first pause
            WhenNotTyping(() => ShowToast(msg.Logo != null ? "" : "", msg.Title, string.IsNullOrEmpty(msg.Body) ? msg.App : msg.Body,
                () => FocusApp(msg.App), badge, ms: 5000, keep: true));
        }
    }

    private void ClearUnread() => _unread = 0;

    private static void FocusApp(string app)
    {
        var token = app.Split(' ')[0];
        var all = Process.GetProcesses();
        try
        {
            var p = all.FirstOrDefault(x => x.MainWindowHandle != IntPtr.Zero && x.ProcessName.Contains(token, StringComparison.OrdinalIgnoreCase));
            if (p != null) Native.SetForegroundWindow(p.MainWindowHandle);
        }
        finally { foreach (var p in all) p.Dispose(); }
    }

    /// <summary>A program by its name is running (the process objects are freed at once).</summary>
    private static bool Running(string name)
    {
        var all = Process.GetProcessesByName(name);
        foreach (var p in all) p.Dispose();
        return all.Length > 0;
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
            // an event with a call link: one click on the notice joins the call
            string? link = e.Link;
            Action open = link != null ? () => SafeShell.OpenUrl(link) : () => OpenTab(TabCalendar);
            string sub = link != null ? e.Title + " · " + L.T("нажмите, чтобы войти") : e.Title;
            if (!e.IsReminder && left <= TimeSpan.FromMinutes(10) && left > TimeSpan.Zero && _eventNotified.Add(key + "10"))
                ShowToast(link != null ? "\uE714" : "", L.F("Через {0} мин", (int)Math.Ceiling(left.TotalMinutes)), sub, open, ms: link != null ? 9000 : 6000, keep: true);
            if (left <= TimeSpan.Zero && left > TimeSpan.FromMinutes(-2) && _eventNotified.Add(key + "0"))
            {
                PlayChime();
                ShowToast(e.IsReminder ? "" : link != null ? "\uE714" : "", e.IsReminder ? L.T("Напоминание") : L.T("Начинается"), sub,
                    open, (Brush)FindResource("Warm"), ms: link != null ? 12000 : 8000, keep: true);
            }
        }

        // old reminders clean themselves up
        int before = _settings.Reminders.Count;
        _settings.Reminders.RemoveAll(r => r.When < now.AddHours(-12));
        if (_settings.Reminders.Count != before) _settings.Save();
    }

    private static void PlayChime()
    {
        if (!Settings.Current.Sounds) return;
        try
        {
            var wav = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Windows Notify Calendar.wav");
            if (File.Exists(wav)) new System.Media.SoundPlayer(wav).Play();
            else System.Media.SystemSounds.Asterisk.Play();
        }
        catch (Exception ex) { Log.Write("calendar: sound: " + ex.GetType().Name + ": " + ex.Message); }
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
            GpuCaption.Text = string.IsNullOrEmpty(s.GpuName) ? L.T("ВИДЕОКАРТА") : s.GpuName.ToUpperInvariant();
            RamText.Text = L.F("{0:0.0} / {1:0} ГБ", s.RamUsedGb, s.RamTotalGb);
            RamBar.ScaleX = s.Ram / 100;
        }

        RefreshMixer();
        DiscordStatus.Text = _discordInCall ? (_discordSpeaking ? L.T("В канале · кто-то говорит") : L.T("В голосовом канале")) : L.T("Не в голосовом канале");
        DiscordBadge.Opacity = _discordInCall ? 1 : 0.5;
        var mic = _audio.MicMuted;
        MicMuteButton.Content = mic == true ? L.T("Микрофон: выкл") : L.T("Микрофон: вкл");
        var discord = _audio.AppState("discord");
        DiscordMuteButton.Content = !discord.Found ? L.T("Discord не запущен") : discord.Muted ? L.T("Звук Discord: выкл") : L.T("Звук Discord: вкл");
        DiscordMuteButton.IsEnabled = discord.Found;

        FocusStatus.Text = FocusMode.IsOn
            ? L.F("До {0:HH:mm} · баннеры Windows скрыты", FocusMode.Until)
            : L.T("Выключено");

        var parts = new List<string>();
        if (_privacy.Mic.Count > 0) parts.Add("🎙 " + string.Join(", ", _privacy.Mic));
        if (_privacy.Camera.Count > 0) parts.Add("📷 " + string.Join(", ", _privacy.Camera));
        PrivacyText.Text = parts.Count > 0 ? string.Join("\n", parts) : L.T("Никто не использует");

        if (full && DateTime.Now - _batteryAt > TimeSpan.FromSeconds(30))
        {
            _batteryAt = DateTime.Now;
            BatteryText.Text = L.T("Проверяю…");
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
                    ? L.T("Logitech G435 не передаёт заряд компьютеру — даже G HUB его не показывает. Здесь появятся устройства, которые умеют сообщать заряд.")
                    : L.T("Нет устройств, которые сообщают заряд.");
            if (_demoMedia != null) BatteryText.Text = L.T("Нет устройств, которые сообщают заряд.");
            Remeasure();
        }
    }

    private void MicMute_Click(object sender, RoutedEventArgs e)
    {
        _audio.MicMuted = !(_audio.MicMuted ?? false);
        ShowToastIfCompact(_audio.MicMuted == true ? "" : "", _audio.MicMuted == true ? L.T("Микрофон выключен") : L.T("Микрофон включён"));
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

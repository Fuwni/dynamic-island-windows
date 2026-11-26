using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>Music: now playing, lyrics, app volume, equalizer and the liquid wave.</summary>
public partial class MainWindow
{
    private readonly MediaService _media = new();
    private readonly AudioService _audio = new();
    private readonly LyricsService _lyrics = new();
    private MediaSnapshot? _lastMedia;
    private Lyrics? _currentLyrics;
    private string _lyricsKey = "";
    /// <summary>Lyrics follow this clock, not the player's rare and jumpy reports.</summary>
    private readonly PlaybackClock _lyricsClock = new();
    private int _lyricIndex = int.MinValue;
    private bool _eqRunning;
    private bool _volumeUpdating;

    private async Task InitMediaAsync()
    {
        _media.Changed += () => Dispatcher.BeginInvoke(UpdateMediaUi);
        ArtRecord.Toggled += () => _ = _media.TogglePlayPause();
        SeekSlider.Format = v => _lastMedia is { } sm && sm.Duration > TimeSpan.Zero ? FormatTime(TimeSpan.FromSeconds(v * sm.Duration.TotalSeconds)) : "0:00";
        SeekSlider.Released += v =>
        {
            if (_lastMedia is not { } sm || sm.Duration <= TimeSpan.Zero) return;
            var to = TimeSpan.FromSeconds(sm.Duration.TotalSeconds * v);
            // pictures and tests: the demo song moves, the user's real player is left alone
            if (_demoMedia != null) { _demoMedia = _demoMedia with { Position = to, PositionUpdatedAt = DateTimeOffset.Now }; UpdateMediaUi(); return; }
            _ = _media.Seek(to);
        };
        // while the thumb is held, the time on the left is where the song will jump to
        SeekSlider.ValueChanged += (_, e) =>
        {
            if (!SeekSlider.IsDragging || _lastMedia is not { } sm || sm.Duration <= TimeSpan.Zero) return;
            var at = TimeSpan.FromSeconds(e.NewValue * sm.Duration.TotalSeconds);
            Elapsed.Text = FormatTime(at);
            Remaining.Text = "-" + FormatTime(sm.Duration - at);
        };
        Safely("music player", InitMusicPlayer);
        try { await _media.InitAsync(); } catch { /* media API unavailable */ }
        UpdateMediaUi();
    }

    private System.Windows.Media.Imaging.BitmapImage? _lastArt;
    private DateTime? _pausedSince;
    private static readonly TimeSpan PausedHideAfter = TimeSpan.FromMinutes(2);

    private bool PausedTooLong => _pausedSince is DateTime p && DateTime.Now - p > PausedHideAfter;

    private void UpdateMediaUi()
    {
        var m = _demoIdle ? null : _demoMedia ?? _media.Current;
        // a forgotten paused tab shouldn't sit on the island forever
        if (m != null && !m.IsPlaying)
        {
            _pausedSince ??= DateTime.Now;
            if (PausedTooLong) m = null;
        }
        else _pausedSince = null;
        bool wasPlaying = _lastMedia?.IsPlaying == true;
        _lastMedia = m;
        bool playing = m?.IsPlaying == true;
        bool hasTrack = m != null && (m.Title.Length > 0 || m.Artist.Length > 0);

        TrackTitle.Text = HomeTrack.Text = BarTitle.Text = hasTrack ? (m!.Title.Length > 0 ? m.Title : L.T("Без названия")) : L.T("Ничего не играет");
        TrackArtist.Text = HomeArtist.Text = BarArtist.Text = hasTrack ? m!.Artist : L.T("Включите музыку в любом плеере");
        TrackSource.Text = hasTrack ? m!.SourceApp : "";

        if (m?.Art != null)
        {
            var brush = new ImageBrush(m.Art) { Stretch = Stretch.UniformToFill };
            MiniArt.Background = HomeArt.Background = BarArt.Background = brush;
            HomeArtPlaceholder.Visibility = Visibility.Collapsed;
        }
        else
        {
            MiniArt.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "ChipBg");
            HomeArt.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "ChipBg");
            BarArt.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "ChipBg");
            HomeArtPlaceholder.Visibility = Visibility.Visible;
        }
        ArtRecord.SetCover(m?.Art);

        // equaliser, glow and character accents take the cover's colour
        if (!ReferenceEquals(m?.Art, _lastArt))
        {
            _lastArt = m?.Art;
            _albumColor = _lastArt != null ? DominantColor(_lastArt) : null;
            foreach (var bar in EqBars.Children.OfType<Rectangle>())
            {
                if (_albumColor is Color ac) bar.Fill = new SolidColorBrush(ac);
                else bar.SetResourceReference(Shape.FillProperty, "Accent");
            }
        }

        ArtRecord.SetPlaying(playing);
        TrackTitle.Playing = playing;
        MusicBars.SetPlaying(playing);
        PlayPauseButton.Content = HomePlay.Content = BarPlay.Content = playing ? "" : "";
        UpdateEyesVisibility();
        SetEqualizer(playing && !_gameFront);
        if (playing) StartRendering(); // liquid wave

        if (wasPlaying != playing)
        {
            Kick(2.5); // play / pause splashes
            if (_mode == Mode.Compact) MorphTo(TargetSize(Mode.Compact));
        }

        var key = hasTrack ? $"{m!.Artist}\n{m.Title}" : "";
        if (key != _lyricsKey)
        {
            if (_lyricsKey.Length > 0 && key.Length > 0)
            {
                Kick(3.5); // new track
                Character.Cheer();
                PulseGlow(_albumColor ?? Color.FromRgb(0x30, 0xD1, 0x58));
            }
            _lyricsKey = key;
            _currentLyrics = null;
            _lyricIndex = int.MinValue;
            ShowLyrics(false, hasTrack ? L.T("Ищу слова…") : L.T("Здесь появятся слова песни"));
            if (_demoMedia != null) { _currentLyrics = DemoLyrics; _lyricsLoaded = key; ShowLyrics(true, ""); }
            else if (hasTrack && _player.Radio && MediaService.IsOwn(m!.AppId)) ShowLyrics(false, L.T("Радио: слов нет"));
            else if (hasTrack) _ = LoadLyricsAsync(m!, key);
            if (_mode == Mode.Expanded && TabMusic.IsChecked == true) RefreshVolume();
        }

        RefreshMusicExtras();
        UpdateProgress();
        UpdateLyricLine();
    }

    private void UpdateProgress()
    {
        var m = _lastMedia;
        if (m == null || m.Duration <= TimeSpan.Zero)
        {
            if (!SeekSlider.IsDragging) SeekSlider.Value = 0;
            ArtRecord.SetProgress(0);
            Elapsed.Text = "0:00";
            Remaining.Text = "-0:00";
            return;
        }
        var pos = m.CurrentPosition;
        double frac = pos.TotalSeconds / m.Duration.TotalSeconds;
        ArtRecord.SetProgress(frac);
        if (SeekSlider.IsDragging) return;   // the thumb is held: the times show where the song will jump to
        SeekSlider.Value = Math.Clamp(frac, 0, 1);
        Elapsed.Text = FormatTime(pos);
        Remaining.Text = "-" + FormatTime(m.Duration - pos);
    }

    private static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    private void SetEqualizer(bool on)
    {
        if (on == _eqRunning) return;
        _eqRunning = on;
        foreach (var bar in EqBars.Children.OfType<Rectangle>())
        {
            var scale = (ScaleTransform)bar.RenderTransform;
            if (on)
            {
                scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                    Slow(new DoubleAnimation(0.25, 1.0, TimeSpan.FromMilliseconds(_rng.Next(280, 560)))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase(),
                    }, 30));
            }
            else
            {
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleY = 0.4;
            }
        }
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e) => _ = _media.TogglePlayPause();
    private void Next_Click(object sender, RoutedEventArgs e) => _ = _media.Next();
    private void Prev_Click(object sender, RoutedEventArgs e) => _ = _media.Previous();

    private void HomeMusic_Click(object sender, MouseButtonEventArgs e)
    {
        if (!_justDragged) TabMusic.IsChecked = true;
    }

    // ------------------------------------------------------------------ volume

    private void RefreshVolume()
    {
        var app = _lastMedia?.AppId ?? "";
        var (vol, perApp) = _audio.GetVolume(app);
        _volumeUpdating = true;
        VolumeSlider.Value = vol;
        _volumeUpdating = false;
        VolumeLabel.Text = perApp && _lastMedia != null
            ? $"{_lastMedia.SourceApp} · {Math.Round(vol * 100)}%"
            : L.F("Общая · {0}%", Math.Round(vol * 100));
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_volumeUpdating || !IsLoaded) return;
        var app = _lastMedia?.AppId ?? "";
        _audio.SetVolume(app, (float)e.NewValue);
        var (_, perApp) = _audio.GetVolume(app);
        VolumeLabel.Text = (perApp && _lastMedia != null ? _lastMedia.SourceApp : L.T("Общая")) + $" · {Math.Round(e.NewValue * 100)}%";
    }

    // ------------------------------------------------------------------ lyrics

    private async Task LoadLyricsAsync(MediaSnapshot track, string key)
    {
        await Task.Delay(400); // duration often arrives a moment after the title
        if (key != _lyricsKey) return;
        var duration = _lastMedia?.Duration ?? track.Duration;

        var lyrics = await _lyrics.GetAsync(track.Artist, track.Title, duration, _lastMedia?.Album ?? track.Album);
        if (key != _lyricsKey) return;

        _currentLyrics = lyrics;
        _lyricsLoaded = key;
        _lyricIndex = int.MinValue;
        ShowLyrics(lyrics is { Lines.Count: > 0 }, lyrics?.Instrumental == true ? L.T("Инструментальная композиция") : L.T("Слова для этой песни не найдены"));
        UpdateLyricLine();
    }

    private void ShowLyrics(bool show, string emptyText)
    {
        LyricsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        LyricsEmpty.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        LyricsEmptyText.Text = emptyText;
    }

    /// <summary>The key of the track whose lyrics search has finished (found or not).</summary>
    private string _lyricsLoaded = "";

    /// <summary>Where the lyrics are now: the smooth clock plus the user's sync shift.</summary>
    private TimeSpan LyricsPosition()
    {
        var m = _lastMedia;
        if (m == null) return TimeSpan.Zero;
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        int resyncs = _lyricsClock.Resyncs;
        _lyricsClock.Report(_lyricsKey, m.CurrentPosition, m.IsPlaying, now);
        if (_lyricsClock.Resyncs != resyncs && _currentLyrics?.Synced == true)
        {
            // why the words run late after a few songs (2.0.1): what the player said and how far the clock was off
            var off = _lyricsClock.LastCorrection;
            Log.Write("lyrics clock: " + (off is { } o
                ? $"{m.AppId}: corrected by {o.TotalMilliseconds:+0;-0} ms at {m.CurrentPosition.TotalSeconds:0.0} s"
                : $"{m.AppId}: took {m.CurrentPosition.TotalSeconds:0.0} s ({(m.IsPlaying ? "playing" : "paused")}), the report is {(DateTimeOffset.Now - m.PositionUpdatedAt).TotalSeconds:0.0} s old"));
        }
        return _lyricsClock.At(now) + TimeSpan.FromMilliseconds(LyricsShiftMs);
    }

    /// <summary>The user's shift plus the player's own lateness (browsers report the position late).</summary>
    private int LyricsShiftMs => _settings.LyricsOffsetMs + PlaybackClock.SourceLeadMs(_lastMedia?.AppId ?? "");

    /// <summary>Jump to a line of the lyrics (the shift is taken back out, so the line starts as it is shown).</summary>
    private void SeekToLyric(TimeSpan lineTime)
    {
        var to = lineTime - TimeSpan.FromMilliseconds(LyricsShiftMs);
        if (to < TimeSpan.Zero) to = TimeSpan.Zero;
        if (Settings.ReadOnly || _demoMedia != null) { if (_lastMedia != null) _lastMedia = _lastMedia with { Position = to, PositionUpdatedAt = DateTimeOffset.Now }; }
        else _ = _media.Seek(to);
        _lyricsClock.Seeked(to, System.Diagnostics.Stopwatch.GetTimestamp());
        UpdateLyricLine();
        TickMusicLyrics();
    }

    private void UpdateLyricLine()
    {
        var lyrics = _currentLyrics;
        var m = _lastMedia;
        if (lyrics == null || m == null || lyrics.Lines.Count == 0) return;

        var lines = lyrics.Lines;
        var at = LyricsPosition();
        int index;
        if (lyrics.Synced) index = LyricsService.LineAt(lines, at + TimeSpan.FromMilliseconds(200));
        else
        {
            double frac = m.Duration > TimeSpan.Zero ? at.TotalSeconds / m.Duration.TotalSeconds : 0;
            index = Math.Clamp((int)(frac * lines.Count), 0, lines.Count - 1);
        }

        if (lyrics.Synced) Karaoke.Sync(at, m.IsPlaying);
        if (index == _lyricIndex) return;
        bool forward = index > _lyricIndex && _lyricIndex != int.MinValue;
        _lyricIndex = index;

        // the folded island: the new line springs in, and the island springs to its width at that very moment
        if (lyrics.Synced && index >= 0 && lines[index].Text.Length > 0)
        {
            var next = index + 1 < lines.Count ? lines[index + 1].Time : lines[index].Time + TimeSpan.FromSeconds(4);
            Karaoke.SetLine(lines[index].Text, lines[index].Time, next);
            if (Karaoke.IsVisible && _mode == Mode.Compact && !_hidden && !_editMode)
            {
                CompactView.InvalidateMeasure();   // the line's new width counts now, not after the next layout pass
                MorphTo(PillSize(CompactWidth));
                _w.Stiffness = 230; _w.Damping = 19;   // livelier than usual: a little past the size and back
                Kick(0.7);
            }
        }

        string Line(int i) => i >= 0 && i < lines.Count ? (lines[i].Text.Length > 0 ? lines[i].Text : "♪") : "";

        LyricPrev.Text = Line(index - 1);
        LyricCur.Text = index < 0 ? "♪" : Line(index);
        LyricNext.Text = Line(index + 1);
        LyricNext2.Text = Line(index + 2);

        if (forward)
        {
            var dur = TimeSpan.FromMilliseconds(260);
            LyricCur.BeginAnimation(OpacityProperty, new DoubleAnimation(0.15, 1, dur));
            LyricCurShift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(10, 0, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
    }
}

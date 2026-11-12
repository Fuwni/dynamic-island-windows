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
    private int _lyricIndex = int.MinValue;
    private bool _eqRunning;
    private bool _volumeUpdating;

    private async Task InitMediaAsync()
    {
        _media.Changed += () => Dispatcher.BeginInvoke(UpdateMediaUi);
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

        TrackTitle.Text = HomeTrack.Text = hasTrack ? (m!.Title.Length > 0 ? m.Title : L.T("Без названия")) : L.T("Ничего не играет");
        TrackArtist.Text = HomeArtist.Text = hasTrack ? m!.Artist : L.T("Включите музыку в любом плеере");
        TrackSource.Text = hasTrack ? m!.SourceApp : "";

        if (m?.Art != null)
        {
            var brush = new ImageBrush(m.Art) { Stretch = Stretch.UniformToFill };
            ArtBox.Background = MiniArt.Background = HomeArt.Background = brush;
            ArtPlaceholder.Visibility = HomeArtPlaceholder.Visibility = Visibility.Collapsed;
        }
        else
        {
            ArtBox.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "ChipBg");
            MiniArt.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "ChipBg");
            HomeArt.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "ChipBg");
            ArtPlaceholder.Visibility = HomeArtPlaceholder.Visibility = Visibility.Visible;
        }

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

        PlayPauseButton.Content = HomePlay.Content = playing ? "" : "";
        UpdateEyesVisibility();
        SetEqualizer(playing);
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
            if (_demoMedia != null) { _currentLyrics = DemoLyrics; ShowLyrics(true, ""); }
            else if (hasTrack) _ = LoadLyricsAsync(m!, key);
            if (_mode == Mode.Expanded && TabMusic.IsChecked == true) RefreshVolume();
        }

        UpdateProgress();
        UpdateLyricLine();
    }

    private void UpdateProgress()
    {
        var m = _lastMedia;
        if (m == null || m.Duration <= TimeSpan.Zero)
        {
            ProgressFill.Width = 0;
            Elapsed.Text = "0:00";
            Remaining.Text = "-0:00";
            return;
        }
        var pos = m.CurrentPosition;
        double frac = pos.TotalSeconds / m.Duration.TotalSeconds;
        ProgressFill.Width = Math.Max(0, ProgressTrack.ActualWidth * frac);
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
                    new DoubleAnimation(0.25, 1.0, TimeSpan.FromMilliseconds(_rng.Next(280, 560)))
                    {
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever,
                        EasingFunction = new SineEase(),
                    });
            }
            else
            {
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleY = 0.4;
            }
        }
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e) => _media.TogglePlayPause();
    private void Next_Click(object sender, RoutedEventArgs e) => _media.Next();
    private void Prev_Click(object sender, RoutedEventArgs e) => _media.Previous();

    private void HomeMusic_Click(object sender, MouseButtonEventArgs e)
    {
        if (!_justDragged) TabMusic.IsChecked = true;
    }

    private void ProgressTrack_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var m = _lastMedia;
        if (m == null || m.Duration <= TimeSpan.Zero || ProgressTrack.ActualWidth <= 0) return;
        double frac = Math.Clamp(e.GetPosition(ProgressTrack).X / ProgressTrack.ActualWidth, 0, 1);
        _media.Seek(TimeSpan.FromSeconds(m.Duration.TotalSeconds * frac));
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

        var lyrics = await _lyrics.GetAsync(track.Artist, track.Title, duration);
        if (key != _lyricsKey) return;

        _currentLyrics = lyrics;
        _lyricIndex = int.MinValue;
        ShowLyrics(lyrics != null, L.T("Слова для этой песни не найдены"));
        UpdateLyricLine();
    }

    private void ShowLyrics(bool show, string emptyText)
    {
        LyricsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        LyricsEmpty.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        LyricsEmptyText.Text = emptyText;
    }

    private void UpdateLyricLine()
    {
        var lyrics = _currentLyrics;
        var m = _lastMedia;
        if (lyrics == null || m == null) return;

        var lines = lyrics.Lines;
        int index;
        if (lyrics.Synced)
        {
            var pos = m.CurrentPosition + TimeSpan.FromMilliseconds(200);
            index = -1;
            for (int i = 0; i < lines.Count && lines[i].Time <= pos; i++) index = i;
        }
        else
        {
            double frac = m.Duration > TimeSpan.Zero ? m.CurrentPosition.TotalSeconds / m.Duration.TotalSeconds : 0;
            index = Math.Clamp((int)(frac * lines.Count), 0, lines.Count - 1);
        }

        if (index == _lyricIndex) return;
        bool forward = index > _lyricIndex && _lyricIndex != int.MinValue;
        _lyricIndex = index;

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

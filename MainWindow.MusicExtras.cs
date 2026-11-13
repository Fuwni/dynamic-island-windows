using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// Music page extras: a wave that moves with the sound, repeat (for players that allow it through
/// Windows media controls) and a heart that keeps the tracks you like in Atoll's own favourites.
/// </summary>
public partial class MainWindow
{
    private double _wavePhase, _waveAmp;

    /// <summary>Called ~30 times a second while the music page is open.</summary>
    private void UpdateMusicWave(double dt)
    {
        double w = MusicWave.ActualWidth, h = MusicWave.Height;
        if (w < 10 || double.IsNaN(h)) return;
        bool playing = _lastMedia?.IsPlaying == true;
        double peak = playing ? Math.Min(1, (_demoMedia != null ? 0.35 + 0.25 * Math.Sin(_wavePhase * 1.7) : _audio.Peak) * 1.4) : 0;
        _waveAmp += ((playing ? 2 + peak * 9 : 0.6) - _waveAmp) * Math.Min(1, dt * 8);
        _wavePhase += dt * (playing ? 3.2 + peak * 5 : 0.8);

        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            double mid = h / 2;
            for (double x = 0; x <= w; x += 4)
            {
                double t = x / w;
                double edge = Math.Sin(t * Math.PI);                 // calm at both ends
                double y = mid + edge * _waveAmp * (Math.Sin(t * 13 + _wavePhase) * 0.65 + Math.Sin(t * 29 - _wavePhase * 1.4) * 0.35);
                if (x == 0) c.BeginFigure(new Point(x, y), false, false);
                else c.LineTo(new Point(x, y), true, true);
            }
        }
        g.Freeze();
        MusicWave.Data = g;
    }

    /// <summary>Repeat and heart follow the current track.</summary>
    private void RefreshMusicExtras()
    {
        var m = _lastMedia;
        bool hasTrack = m != null && (m.Title.Length > 0 || m.Artist.Length > 0);

        // players that let Windows change repeat (most browsers, AIMP…) get it directly; for the rest
        // (Spotify, Yandex Music…) Atoll repeats the track itself
        bool native = _demoMedia == null && _media.CanRepeat;
        var repeat = _demoMedia != null ? Windows.Media.MediaPlaybackAutoRepeatMode.List
                   : native ? _media.Repeat
                   : _ownRepeatKey != null ? Windows.Media.MediaPlaybackAutoRepeatMode.Track : Windows.Media.MediaPlaybackAutoRepeatMode.None;
        RepeatButton.Visibility = hasTrack ? Visibility.Visible : Visibility.Hidden;
        RepeatButton.Content = repeat == Windows.Media.MediaPlaybackAutoRepeatMode.Track ? "" : "";
        RepeatButton.Opacity = repeat is Windows.Media.MediaPlaybackAutoRepeatMode.List or Windows.Media.MediaPlaybackAutoRepeatMode.Track ? 1 : 0.45;
        RepeatButton.ToolTip = repeat switch
        {
            Windows.Media.MediaPlaybackAutoRepeatMode.List => L.T("Повтор: весь список"),
            Windows.Media.MediaPlaybackAutoRepeatMode.Track => L.T("Повтор: этот трек"),
            _ => L.T("Повтор выключен"),
        };

        LikeButton.Visibility = hasTrack ? Visibility.Visible : Visibility.Hidden;
        bool liked = hasTrack && _settings.LikedTracks.Contains(LikeKey(m!));
        LikeButton.Content = liked ? "" : "";
        LikeButton.Foreground = liked ? new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x8A)) : (Brush)FindResource("Fg");
        LikeButton.ToolTip = liked ? L.T("Убрать из избранного") : L.T("В избранное");
    }

    private static string LikeKey(MediaSnapshot m) => m.Artist.Length > 0 ? $"{m.Artist} — {m.Title}" : m.Title;

    // ---------------------------------------------------------------- Atoll's own "repeat this track"

    private string? _ownRepeatKey;           // the track being repeated, when the player can't do it itself
    private DateTime _ownRepeatActionAt;

    private void Repeat_Click(object sender, RoutedEventArgs e)
    {
        Kick(1.5);
        if (_demoMedia == null && _media.CanRepeat) { _media.CycleRepeat(); return; }
        var m = _lastMedia;
        if (m == null) return;
        _ownRepeatKey = _ownRepeatKey == null ? LikeKey(m) : null;
        ShowToast(_ownRepeatKey != null ? "" : "", _ownRepeatKey != null ? L.T("Повтор: этот трек") : L.T("Повтор выключен"), _ownRepeatKey);
        RefreshMusicExtras();
    }

    /// <summary>~4 times a second: keep the repeated track playing.</summary>
    private void KeepRepeating()
    {
        var key = _ownRepeatKey;
        var m = _lastMedia;
        if (key == null || m == null || _demoMedia != null) return;
        if (DateTime.Now - _ownRepeatActionAt < TimeSpan.FromSeconds(2.5)) return;
        if (LikeKey(m) == key)
        {
            // just before the end: back to the start
            if (m.IsPlaying && m.Duration > TimeSpan.FromSeconds(5) && m.Duration - m.CurrentPosition < TimeSpan.FromSeconds(1.2))
            {
                _ownRepeatActionAt = DateTime.Now;
                _media.Seek(TimeSpan.Zero);
            }
        }
        else if (m.IsPlaying && m.CurrentPosition < TimeSpan.FromSeconds(4))
        {
            // the player moved on anyway (it doesn't allow jumping back): one step back
            _ownRepeatActionAt = DateTime.Now;
            _media.Previous();
        }
        else _ownRepeatKey = null;   // the user picked something else: repeat ends
    }

    private void Like_Click(object sender, RoutedEventArgs e)
    {
        var m = _lastMedia;
        if (m == null) return;
        var key = LikeKey(m);
        if (_settings.LikedTracks.Remove(key))
            ShowToast("", L.T("Убрано из избранного"), key);
        else
        {
            _settings.LikedTracks.Insert(0, key);
            if (_settings.LikedTracks.Count > 500) _settings.LikedTracks.RemoveAt(_settings.LikedTracks.Count - 1);
            ShowToast("", L.T("Добавлено в избранное"), key, null, new SolidColorBrush(Color.FromRgb(0xFF, 0x5C, 0x8A)));
            Character.Emote(Views.Emotion.Love);
        }
        _settings.Save();
        RefreshMusicExtras();
        if (LikedPanel.Visibility == Visibility.Visible) BuildLikedList();
    }

    private void LikedToggle_Click(object sender, RoutedEventArgs e)
    {
        bool show = LikedPanel.Visibility != Visibility.Visible;
        LikedPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        LyricsPanel.Opacity = LyricsEmpty.Opacity = show ? 0 : 1;
        if (show) BuildLikedList();
    }

    /// <summary>Liked tracks; a click copies "Artist — Title" (to find it in any service).</summary>
    private void BuildLikedList()
    {
        LikedList.Children.Clear();
        if (_settings.LikedTracks.Count == 0)
        {
            LikedList.Children.Add(new TextBlock { Text = L.T("Нажмите ♥ под треком, и он появится здесь"), Style = (Style)FindResource("Muted"), TextWrapping = TextWrapping.Wrap });
            return;
        }
        foreach (var t in _settings.LikedTracks.Take(100))
        {
            var b = new Button
            {
                Style = (Style)FindResource("GhostPill"), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = new TextBlock { Text = t, TextTrimming = TextTrimming.CharacterEllipsis }, ToolTip = L.T("Скопировать название"),
            };
            var text = t;
            b.Click += (_, _) =>
            {
                try { Clipboard.SetText(text); ShowToast("", L.T("Скопировано"), text); } catch { }
            };
            LikedList.Children.Add(b);
        }
    }
}

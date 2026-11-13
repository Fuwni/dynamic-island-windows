using System.IO;
using System.Windows.Media.Imaging;
using global::Windows.Media.Control;
using global::Windows.Storage.Streams;

namespace DynamicIsland.Services;

public sealed record MediaSnapshot(
    string Title,
    string Artist,
    string SourceApp,
    string AppId,
    BitmapImage? Art,
    bool IsPlaying,
    TimeSpan Position,
    DateTimeOffset PositionUpdatedAt,
    TimeSpan Duration)
{
    public TimeSpan CurrentPosition
    {
        get
        {
            var pos = Position;
            if (IsPlaying && PositionUpdatedAt != default)
                pos += DateTimeOffset.Now - PositionUpdatedAt;
            if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
            if (Duration > TimeSpan.Zero && pos > Duration) pos = Duration;
            return pos;
        }
    }
}

/// <summary>
/// Reads "now playing" from any app that reports to Windows media controls
/// (Spotify, Яндекс Музыка, AIMP, browsers, etc.) and controls playback.
/// </summary>
public sealed class MediaService
{
    private readonly object _lock = new();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;

    private string _title = "", _artist = "", _source = "", _appId = "";
    private LegacyPlayers.Track? _legacy;
    private BitmapImage? _art;
    private bool _playing;
    private TimeSpan _position, _duration;
    private DateTimeOffset _positionAt;

    public event Action? Changed;

    public MediaSnapshot? Current
    {
        get
        {
            lock (_lock)
            {
                if (_session == null)
                {
                    var l = _legacy;
                    return l == null ? null
                        : new MediaSnapshot(l.Title, l.Artist, l.App, l.App, null, l.Playing,
                            TimeSpan.Zero, default, TimeSpan.Zero);
                }
                return new MediaSnapshot(_title, _artist, _source, _appId, _art, _playing, _position, _positionAt, _duration);
            }
        }
    }

    /// <summary>Call ~once a second: picks up AIMP / Winamp when no modern player is active.</summary>
    public void PollLegacy()
    {
        // Windows' "current" session can be a paused tab while another app plays - follow the one that plays
        if (_manager != null && !_playing)
        {
            try
            {
                var playing = _manager.GetSessions().FirstOrDefault(s =>
                    s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
                if (playing != null && playing != _session) Attach(playing);
            }
            catch { }
        }

        if (_session != null) { _legacy = null; return; }
        var now = LegacyPlayers.Detect();
        if (now != _legacy)
        {
            _legacy = now;
            Changed?.Invoke();
        }
    }

    public async Task InitAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.CurrentSessionChanged += (m, _) =>
        {
            // don't jump away from something that plays to a paused session
            var cur = m.GetCurrentSession();
            if (_playing && _session != null && cur != null && cur != _session &&
                cur.GetPlaybackInfo().PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) return;
            Attach(cur);
        };
        Attach(_manager.GetCurrentSession());
    }

    private void Attach(GlobalSystemMediaTransportControlsSession? session)
    {
        lock (_lock)
        {
            if (_session != null)
            {
                _session.MediaPropertiesChanged -= OnPropertiesChanged;
                _session.PlaybackInfoChanged -= OnPlaybackChanged;
                _session.TimelinePropertiesChanged -= OnTimelineChanged;
            }

            _session = session;
            _title = _artist = _source = _appId = "";
            _art = null;
            _playing = false;
            _position = _duration = TimeSpan.Zero;
            _positionAt = default;

            if (session != null)
            {
                session.MediaPropertiesChanged += OnPropertiesChanged;
                session.PlaybackInfoChanged += OnPlaybackChanged;
                session.TimelinePropertiesChanged += OnTimelineChanged;
                _appId = session.SourceAppUserModelId ?? "";
                _source = PrettyAppName(_appId);
                ReadPlayback(session);
                ReadTimeline(session);
            }
        }

        Changed?.Invoke();
        if (session != null) _ = ReadPropertiesAsync(session);
    }

    private void OnPropertiesChanged(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e)
        => _ = ReadPropertiesAsync(s);

    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e)
    {
        lock (_lock) { if (s == _session) ReadPlayback(s); }
        Changed?.Invoke();
    }

    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e)
    {
        lock (_lock) { if (s == _session) ReadTimeline(s); }
        Changed?.Invoke();
    }

    private Windows.Media.MediaPlaybackAutoRepeatMode? _repeat;
    private bool _canRepeat;

    /// <summary>The player's repeat mode (null when it doesn't tell) and whether it lets us change it.</summary>
    public Windows.Media.MediaPlaybackAutoRepeatMode? Repeat { get { lock (_lock) return _session != null ? _repeat : null; } }
    public bool CanRepeat { get { lock (_lock) return _session != null && _canRepeat; } }

    /// <summary>Off → whole list → this track → off. Players that don't support it simply ignore the request.</summary>
    public async void CycleRepeat()
    {
        try
        {
            var s = _session;
            if (s == null) return;
            var next = _repeat switch
            {
                Windows.Media.MediaPlaybackAutoRepeatMode.List => Windows.Media.MediaPlaybackAutoRepeatMode.Track,
                Windows.Media.MediaPlaybackAutoRepeatMode.Track => Windows.Media.MediaPlaybackAutoRepeatMode.None,
                _ => Windows.Media.MediaPlaybackAutoRepeatMode.List,
            };
            if (await s.TryChangeAutoRepeatModeAsync(next)) { lock (_lock) _repeat = next; Changed?.Invoke(); }
        }
        catch { }
    }

    private void ReadPlayback(GlobalSystemMediaTransportControlsSession s)
    {
        try
        {
            var wasPlaying = _playing;
            var info = s.GetPlaybackInfo();
            _repeat = info.AutoRepeatMode;
            _canRepeat = info.Controls.IsRepeatEnabled;
            _playing = info.PlaybackStatus ==
                       GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            // Freeze/unfreeze the extrapolated position at the moment state flips.
            if (wasPlaying && !_playing && _positionAt != default)
            {
                _position += DateTimeOffset.Now - _positionAt;
                _positionAt = DateTimeOffset.Now;
            }
            else if (!wasPlaying && _playing)
            {
                _positionAt = DateTimeOffset.Now;
            }
        }
        catch { /* session went away */ }
    }

    private void ReadTimeline(GlobalSystemMediaTransportControlsSession s)
    {
        try
        {
            var t = s.GetTimelineProperties();
            _position = t.Position;
            _duration = t.EndTime - t.StartTime;
            _positionAt = t.LastUpdatedTime;
        }
        catch { /* session went away */ }
    }

    private async Task ReadPropertiesAsync(GlobalSystemMediaTransportControlsSession s)
    {
        try
        {
            var props = await s.TryGetMediaPropertiesAsync();
            if (props == null) return;
            var art = props.Thumbnail != null ? await LoadArtAsync(props.Thumbnail) : null;

            lock (_lock)
            {
                if (s != _session) return;
                _title = props.Title ?? "";
                _artist = props.Artist ?? "";
                _art = art;
            }
            Changed?.Invoke();
        }
        catch { /* player refused or closed */ }
    }

    private static async Task<BitmapImage?> LoadArtAsync(IRandomAccessStreamReference reference)
    {
        try
        {
            using var ras = await reference.OpenReadAsync();
            using var stream = ras.AsStreamForRead();
            var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 200;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public async void TogglePlayPause()
    {
        try
        {
            if (_session != null) await _session.TryTogglePlayPauseAsync();
            else if (_legacy != null) LegacyPlayers.PlayPause(_legacy);
        }
        catch { }
    }

    public async void Next()
    {
        try
        {
            if (_session != null) await _session.TrySkipNextAsync();
            else if (_legacy != null) LegacyPlayers.Next(_legacy);
        }
        catch { }
    }

    public async void Previous()
    {
        try
        {
            if (_session != null) await _session.TrySkipPreviousAsync();
            else if (_legacy != null) LegacyPlayers.Previous(_legacy);
        }
        catch { }
    }

    public async void Seek(TimeSpan position)
    {
        try { if (_session != null) await _session.TryChangePlaybackPositionAsync(position.Ticks); } catch { }
    }

    private static string PrettyAppName(string aumid)
    {
        if (string.IsNullOrEmpty(aumid)) return "";
        // installed web apps: "Chrome._crx_<id>", "MSEdge._crx_<id>"
        var crx = aumid.IndexOf("._crx_", StringComparison.OrdinalIgnoreCase);
        if (crx > 0) return (aumid[..crx].Equals("MSEdge", StringComparison.OrdinalIgnoreCase) ? "Edge" : aumid[..crx]) + L.T(" · веб-приложение");
        var name = aumid;
        var bang = name.LastIndexOf('!');
        if (bang >= 0) name = name[(bang + 1)..];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        name = name.Replace("App", "", StringComparison.Ordinal).Trim();
        if (name.Length == 0) return "";
        // Firefox and some others report a hash instead of a name.
        if (name.Length >= 12 && name.All(Uri.IsHexDigit)) return L.T("Браузер");
        return char.ToUpper(name[0]) + name[1..];
    }
}

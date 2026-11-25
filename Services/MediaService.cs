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
    /// <summary>The album, when the player tells it (lyrics are found more exactly with it).</summary>
    public string Album { get; init; } = "";

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
public sealed class MediaService : IDisposable
{
    private readonly object _lock = new();
    private readonly AimpRemote _aimp = new();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;

    private string _title = "", _artist = "", _source = "", _appId = "", _album = "";
    private LegacyPlayers.Track? _legacy;
    private BitmapImage? _art;
    private bool _playing;
    private TimeSpan _position, _duration;
    private DateTimeOffset _positionAt;
    private bool _useAimp;

    public event Action? Changed;

    private static bool IsAimpSmtc(string? aumid) =>
        !string.IsNullOrEmpty(aumid) && aumid.Contains("aimp", StringComparison.OrdinalIgnoreCase);

    /// <summary>A session the user picked: followed even while another one plays (until it goes away).</summary>
    private string _chosen = "";

    public void SetHostWindow(IntPtr hwnd) => _aimp.SetListenerWindow(hwnd);

    public bool HandleWindowMessage(int msg, IntPtr wParam, IntPtr lParam) =>
        _aimp.HandleWindowMessage(msg, wParam, lParam);

    /// <summary>Every app reporting to Windows media controls now: its media id, a name to show, whether it plays.</summary>
    public IReadOnlyList<(string AppId, string Name, bool Playing)> Sessions()
    {
        var list = new List<(string AppId, string Name, bool Playing)>();
        bool hasAimp = _aimp.IsConnected && _aimp.Current is { HasTrack: true };
        if (_manager != null)
        {
            try
            {
                foreach (var s in _manager.GetSessions())
                {
                    var id = s.SourceAppUserModelId ?? "";
                    if (hasAimp && IsAimpSmtc(id)) continue;
                    list.Add((id, PrettyAppName(id),
                        s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing));
                }
            }
            catch (Exception e) { Log.Write("media sessions: " + e.Message); }
        }
        if (hasAimp && _aimp.Current is { } aimp)
            list.Add((AimpRemote.AppId, "AIMP", aimp.IsPlaying));
        return list;
    }

    /// <summary>Follow this app's session (the widget's switcher).</summary>
    public void Choose(string appId)
    {
        if (appId == AimpRemote.AppId && _aimp.IsConnected)
        {
            lock (_lock) { _chosen = AimpRemote.AppId; _useAimp = true; }
            Changed?.Invoke();
            return;
        }
        var s = _manager?.GetSessions().FirstOrDefault(x => x.SourceAppUserModelId == appId);
        if (s == null) return;
        lock (_lock) { _chosen = appId; _useAimp = false; }
        Attach(s);
    }

    /// <summary>The next session after the one shown (wraps around).</summary>
    public void ChooseNext()
    {
        var list = Sessions();
        if (list.Count < 2) return;
        string curId;
        lock (_lock) curId = _useAimp ? AimpRemote.AppId : _appId;
        int i = list.ToList().FindIndex(x => x.AppId == curId);
        Choose(list[(i + 1) % list.Count].AppId);
    }

    public MediaSnapshot? Current
    {
        get
        {
            lock (_lock)
            {
                if (_useAimp && _aimp.Current is { HasTrack: true } aimp)
                {
                    return new MediaSnapshot(aimp.Title, aimp.Artist, "AIMP", AimpRemote.AppId, aimp.Art,
                        aimp.IsPlaying, aimp.Position, aimp.PositionUpdatedAt, aimp.Duration) { Album = aimp.Album };
                }
                if (_session == null)
                {
                    var l = _legacy;
                    return l == null ? null
                        : new MediaSnapshot(l.Title, l.Artist, L.T(l.App), l.App, null, l.Playing,
                            TimeSpan.Zero, default, TimeSpan.Zero);
                }
                return new MediaSnapshot(_title, _artist, _source, _appId, _art, _playing, _position, _positionAt, _duration) { Album = _album };
            }
        }
    }

    /// <summary>Call ~once a second: picks up AIMP / Winamp when no modern player is active.</summary>
    public void PollLegacy()
    {
        _aimp.Poll();

        if (_chosen == AimpRemote.AppId && !_aimp.IsConnected) _chosen = "";
        else if (_chosen.Length > 0 && _chosen != AimpRemote.AppId && _manager != null)
        {
            try { if (_manager.GetSessions().All(x => x.SourceAppUserModelId != _chosen)) _chosen = ""; }
            catch { _chosen = ""; }
        }

        var aimp = _aimp.Current;
        bool aimpHasTrack = _aimp.IsConnected && aimp is { HasTrack: true };

        if (_chosen.Length == 0)
        {
            if (!_useAimp && aimpHasTrack && (aimp!.IsPlaying && (!_playing || IsAimpSmtc(_appId)) || _session == null || IsAimpSmtc(_appId)))
            {
                lock (_lock) _useAimp = true;
                Changed?.Invoke();
                return;
            }
            if (_useAimp && aimp is not { IsPlaying: true } && _manager != null)
            {
                try
                {
                    var playing = _manager.GetSessions().FirstOrDefault(s =>
                        !IsAimpSmtc(s.SourceAppUserModelId) &&
                        s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
                    if (playing != null)
                    {
                        lock (_lock) _useAimp = false;
                        Attach(playing);
                        return;
                    }
                }
                catch (Exception ex) { Log.Error("media: sessions", ex); }
            }
            if (!_useAimp && _manager != null && !_playing)
            {
                try
                {
                    var playing = _manager.GetSessions().FirstOrDefault(s =>
                        !(aimpHasTrack && IsAimpSmtc(s.SourceAppUserModelId)) &&
                        s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
                    if (playing != null && playing != _session) Attach(playing);
                }
                catch (Exception ex) { Log.Error("media: sessions", ex); }
            }
        }

        if (_useAimp || _session != null) { _legacy = null; return; }
        var now = LegacyPlayers.Detect(skipAimp: _aimp.IsConnected);
        if (now != _legacy)
        {
            _legacy = now;
            Changed?.Invoke();
        }
    }

    /// <summary>Windows gave us its media sessions or AIMP is connected.</summary>
    public bool Ready => _manager != null || _aimp.IsConnected;

    public async Task InitAsync()
    {
        _aimp.Changed += OnAimpChanged;
        _aimp.Poll();

        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.CurrentSessionChanged += (m, _) =>
        {
            if (_chosen == AimpRemote.AppId && _aimp.IsConnected) return;
            var cur = m.GetCurrentSession();
            if (_chosen.Length > 0 && _session != null && cur?.SourceAppUserModelId != _chosen) return;
            if (_useAimp && _aimp.Current is { IsPlaying: true })
            {
                if (cur == null || IsAimpSmtc(cur.SourceAppUserModelId) ||
                    cur.GetPlaybackInfo().PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    return;
                lock (_lock) _useAimp = false;
            }
            if (_playing && _session != null && cur != null && cur != _session &&
                cur.GetPlaybackInfo().PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) return;
            if (_aimp.IsConnected && _aimp.Current is { HasTrack: true } && cur != null && IsAimpSmtc(cur.SourceAppUserModelId))
            {
                lock (_lock) _useAimp = true;
                Changed?.Invoke();
                return;
            }
            Attach(cur);
        };
        var initial = _manager.GetCurrentSession();
        if (_useAimp && (initial == null || IsAimpSmtc(initial.SourceAppUserModelId) ||
            initial.GetPlaybackInfo().PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing))
        {
            return;
        }
        Attach(initial);
    }

    public void Dispose()
    {
        _aimp.Changed -= OnAimpChanged;
        _aimp.Dispose();
    }

    private void OnAimpChanged()
    {
        var aimp = _aimp.Current;
        bool connected = _aimp.IsConnected;
        bool hasTrack = connected && aimp is { HasTrack: true };
        bool notify = false;

        lock (_lock)
        {
            if (!hasTrack)
            {
                if (_chosen == AimpRemote.AppId && !connected) _chosen = "";
                if (_useAimp)
                {
                    _useAimp = false;
                    notify = true;
                }
            }
            else if (_chosen == AimpRemote.AppId)
            {
                _useAimp = true;
                notify = true;
            }
            else if (_chosen.Length == 0)
            {
                if (_useAimp)
                {
                    notify = true;
                }
                else if (aimp!.IsPlaying && (!_playing || IsAimpSmtc(_appId)) || _session == null || IsAimpSmtc(_appId))
                {
                    _useAimp = true;
                    notify = true;
                }
            }
        }

        if (notify) Changed?.Invoke();
    }

    private void Attach(GlobalSystemMediaTransportControlsSession? session)
    {
        lock (_lock)
        {
            _useAimp = false;
            if (_session != null)
            {
                _session.MediaPropertiesChanged -= OnPropertiesChanged;
                _session.PlaybackInfoChanged -= OnPlaybackChanged;
                _session.TimelinePropertiesChanged -= OnTimelineChanged;
            }

            _session = session;
            _title = _artist = _source = _appId = _album = "";
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
        bool changed = false;
        lock (_lock)
        {
            if (s == _session)
            {
                ReadPlayback(s);
                if (_useAimp && _chosen.Length == 0 && _playing && !IsAimpSmtc(_appId) && _aimp.Current is not { IsPlaying: true })
                    _useAimp = false;
                changed = !_useAimp;
            }
        }
        if (changed) Changed?.Invoke();
    }

    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e)
    {
        bool changed = false;
        lock (_lock)
        {
            if (s == _session)
            {
                ReadTimeline(s);
                changed = !_useAimp;
            }
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>Diagnostics: what the player reports right now, untouched.</summary>
    public string RawTimeline()
    {
        var s = _session;
        if (s == null) return "no session";
        try
        {
            var t = s.GetTimelineProperties();
            return $"app={s.SourceAppUserModelId} pos={t.Position.TotalSeconds:0.000} start={t.StartTime.TotalSeconds:0.0} updated={(DateTimeOffset.Now - t.LastUpdatedTime).TotalSeconds:0.000}s ago";
        }
        catch (Exception e) { return e.Message; }
    }

    private Windows.Media.MediaPlaybackAutoRepeatMode? _repeat;
    private bool _canRepeat;

    /// <summary>The player's repeat mode (null when it doesn't tell) and whether it lets us change it.</summary>
    public Windows.Media.MediaPlaybackAutoRepeatMode? Repeat
    {
        get
        {
            lock (_lock)
            {
                if (_useAimp && _aimp.Current is { HasTrack: true } aimp)
                    return aimp.Repeat ? Windows.Media.MediaPlaybackAutoRepeatMode.Track : Windows.Media.MediaPlaybackAutoRepeatMode.None;
                return _session != null ? _repeat : null;
            }
        }
    }
    public bool CanRepeat { get { lock (_lock) return (_useAimp && _aimp.IsConnected) || (_session != null && _canRepeat); } }

    /// <summary>Off → whole list → this track → off. Players that don't support it simply ignore the request.</summary>
    public async Task CycleRepeat()
    {
        if (_useAimp && _aimp.IsConnected) { _aimp.CycleRepeat(); return; }
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
        catch (Exception ex) { Log.Write("media: repeat: " + ex.GetType().Name + ": " + ex.Message); }
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

            bool notify;
            lock (_lock)
            {
                if (s != _session) return;
                _title = props.Title ?? "";
                _artist = props.Artist ?? "";
                _album = props.AlbumTitle ?? "";
                _art = art;
                notify = !_useAimp;
            }
            if (notify) Changed?.Invoke();
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
            bmp.DecodePixelWidth = 400;   // fills a whole small widget at 150 %
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

    public async Task TogglePlayPause()
    {
        try
        {
            if (_useAimp && _aimp.IsConnected) _aimp.PlayPause();
            else if (_session != null) await _session.TryTogglePlayPauseAsync();
            else if (_legacy != null) LegacyPlayers.PlayPause(_legacy);
        }
        catch (Exception ex) { Log.Write("media: play/pause: " + ex.GetType().Name + ": " + ex.Message); }
    }

    public async Task Next()
    {
        try
        {
            if (_useAimp && _aimp.IsConnected) _aimp.Next();
            else if (_session != null) await _session.TrySkipNextAsync();
            else if (_legacy != null) LegacyPlayers.Next(_legacy);
        }
        catch (Exception ex) { Log.Write("media: next: " + ex.GetType().Name + ": " + ex.Message); }
    }

    public async Task Previous()
    {
        try
        {
            if (_useAimp && _aimp.IsConnected) _aimp.Previous();
            else if (_session != null) await _session.TrySkipPreviousAsync();
            else if (_legacy != null) LegacyPlayers.Previous(_legacy);
        }
        catch (Exception ex) { Log.Write("media: previous: " + ex.GetType().Name + ": " + ex.Message); }
    }

    /// <summary>Atoll's own player: Windows media controls can't move it, so a seek goes to it directly.</summary>
    public Func<TimeSpan, bool>? OwnSeek { get; set; }

    public async Task Seek(TimeSpan position)
    {
        if (_useAimp && _aimp.IsConnected)
        {
            _aimp.Seek(position);
            return;
        }
        if (_session != null && IsOwn(_appId)) 
        {
            if (OwnSeek?.Invoke(position) != true) return;
            lock (_lock) { _position = position; _positionAt = DateTimeOffset.Now; }   // Windows hears of it seconds later
            Changed?.Invoke();
            return;
        }
        try { if (_session != null) await _session.TryChangePlaybackPositionAsync(position.Ticks); } catch (Exception ex) { Log.Write("media: seek: " + ex.GetType().Name + ": " + ex.Message); }
    }

    /// <summary>The service open in Atoll's own player window: Windows only sees "msedgewebview2.exe".</summary>
    public static string WebPlayerName { get; set; } = "";

    /// <summary>Pause this app's session, whichever one the island shows.</summary>
    public async Task Pause(string appId)
    {
        if (appId == AimpRemote.AppId && _aimp.IsConnected)
        {
            _aimp.Pause();
            return;
        }
        try
        {
            foreach (var s in _manager?.GetSessions() ?? [])
                if (s.SourceAppUserModelId == appId) await s.TryPauseAsync();
        }
        catch (Exception ex) { Log.Write("media: pause: " + ex.GetType().Name + ": " + ex.Message); }
    }

    /// <summary>The session of Atoll's own player (radio, files).</summary>
    public static bool IsOwn(string aumid) => aumid.Contains("DynamicIsland", StringComparison.OrdinalIgnoreCase);

    public static string PrettyAppName(string aumid)
    {
        if (string.IsNullOrEmpty(aumid)) return "";
        if (aumid.StartsWith("msedgewebview2", StringComparison.OrdinalIgnoreCase) && WebPlayerName.Length > 0) return WebPlayerName;
        if (IsOwn(aumid)) return "Atoll";
        // the Yandex Music app ("ru.yandex.desktop.music") and its old Store version ("A025C540.Yandex.Music_…!App")
        if (aumid.Contains("yandex", StringComparison.OrdinalIgnoreCase) && aumid.Contains("music", StringComparison.OrdinalIgnoreCase))
            return L.T("Яндекс Музыка");
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

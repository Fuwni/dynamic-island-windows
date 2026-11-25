using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media.Imaging;

namespace DynamicIsland.Services;

public sealed record AimpTrack(
    bool HasTrack,
    string Title,
    string Artist,
    string Album,
    string FileName,
    TimeSpan Duration,
    TimeSpan Position,
    DateTimeOffset PositionUpdatedAt,
    bool IsPlaying,
    bool Repeat,
    BitmapImage? Art);

/// <summary>
/// Reads now-playing metadata, progress, repeat state and album art from AIMP's built-in
/// <c>AIMP2_RemoteInfo</c> window &amp; shared memory (no plugin required) and sends playback commands.
/// </summary>
public sealed class AimpRemote : IDisposable
{
    public const string AppId = "AIMP.exe";
    private const string RemoteClass = "AIMP2_RemoteInfo";
    private const int MapFileSize = 2048;

    private const int WM_COPYDATA = 0x004A;
    private const int WM_AIMP_COMMAND = Native.WM_USER + 0x75;
    private const int WM_AIMP_NOTIFY = Native.WM_USER + 0x76;
    private const int WM_AIMP_PROPERTY = Native.WM_USER + 0x77;
    private const uint WM_AIMP_COPYDATA_ALBUMART_ID = 0x41495043;

    private const int AIMP_RA_PROPVALUE_GET = 0;
    private const int AIMP_RA_PROPVALUE_SET = 1;
    private const int AIMP_RA_PROPERTY_PLAYER_POSITION = 0x20;
    private const int AIMP_RA_PROPERTY_PLAYER_DURATION = 0x30;
    private const int AIMP_RA_PROPERTY_PLAYER_STATE = 0x40;
    private const int AIMP_RA_PROPERTY_TRACK_REPEAT = 0x70;

    private const int AIMP_RA_CMD_REGISTER_NOTIFY = 11;
    private const int AIMP_RA_CMD_UNREGISTER_NOTIFY = 12;
    private const int AIMP_RA_CMD_PLAY = 13;
    private const int AIMP_RA_CMD_PLAYPAUSE = 14;
    private const int AIMP_RA_CMD_PAUSE = 15;
    private const int AIMP_RA_CMD_NEXT = 17;
    private const int AIMP_RA_CMD_PREV = 18;
    private const int AIMP_RA_CMD_GET_ALBUMART = 29;

    private const int AIMP_RA_NOTIFY_TRACK_INFO = 1;
    private const int AIMP_RA_NOTIFY_TRACK_START = 2;
    private const int AIMP_RA_NOTIFY_PROPERTY = 3;

    private const uint FILE_MAP_READ = 0x0004;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenFileMapping(uint dwDesiredAccess, bool bInheritHandle, string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr MapViewOfFile(IntPtr hFileMappingObject, uint dwDesiredAccess, uint dwFileOffsetHigh, uint dwFileOffsetLow, UIntPtr dwNumberOfBytesToMap);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UnmapViewOfFile(IntPtr lpBaseAddress);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct RemoteFileInfoHeader
    {
        public uint Deprecated1;
        public int Active;
        public uint BitRate;
        public uint Channels;
        public uint Duration;
        public long FileSize;
        public uint FileMark;
        public uint SampleRate;
        public uint TrackNumber;
        public uint AlbumLength;
        public uint ArtistLength;
        public uint DateLength;
        public uint FileNameLength;
        public uint GenreLength;
        public uint TitleLength;
        public uint Deprecated2_0;
        public uint Deprecated2_1;
        public uint Deprecated2_2;
        public uint Deprecated2_3;
        public uint Deprecated2_4;
        public uint Deprecated2_5;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    private readonly object _lock = new();
    private IntPtr _aimpHwnd;
    private IntPtr _listenerHwnd;
    private AimpTrack? _current;
    private string _trackKey = "";

    public event Action? Changed;

    public bool IsConnected
    {
        get { lock (_lock) return _aimpHwnd != IntPtr.Zero; }
    }

    public AimpTrack? Current
    {
        get { lock (_lock) return _current; }
    }

    public void SetListenerWindow(IntPtr hwnd)
    {
        lock (_lock)
        {
            if (_listenerHwnd == hwnd) return;
            if (_aimpHwnd != IntPtr.Zero && _listenerHwnd != IntPtr.Zero && Native.IsWindow(_aimpHwnd))
                SendAimp(_aimpHwnd, WM_AIMP_COMMAND, AIMP_RA_CMD_UNREGISTER_NOTIFY, _listenerHwnd);

            _listenerHwnd = hwnd;

            if (_aimpHwnd != IntPtr.Zero && _listenerHwnd != IntPtr.Zero && Native.IsWindow(_aimpHwnd))
                SendAimp(_aimpHwnd, WM_AIMP_COMMAND, AIMP_RA_CMD_REGISTER_NOTIFY, _listenerHwnd);
        }

        if (_aimpHwnd != IntPtr.Zero)
            RefreshAll(requestArt: true);
    }

    public void Dispose()
    {
        IntPtr aimp, listener;
        lock (_lock)
        {
            aimp = _aimpHwnd;
            listener = _listenerHwnd;
            _aimpHwnd = IntPtr.Zero;
            _listenerHwnd = IntPtr.Zero;
            _current = null;
            _trackKey = "";
        }
        if (aimp != IntPtr.Zero && listener != IntPtr.Zero && Native.IsWindow(aimp))
            SendAimp(aimp, WM_AIMP_COMMAND, AIMP_RA_CMD_UNREGISTER_NOTIFY, listener);
    }

    public void Poll()
    {
        var found = Native.FindWindow(RemoteClass, RemoteClass);
        IntPtr oldAimp;
        IntPtr listener;
        bool targetChanged;

        lock (_lock)
        {
            if (found != IntPtr.Zero && !Native.IsWindow(found)) found = IntPtr.Zero;
            targetChanged = found != _aimpHwnd;
            oldAimp = _aimpHwnd;
            listener = _listenerHwnd;
            if (targetChanged) _aimpHwnd = found;
        }

        if (targetChanged)
        {
            if (oldAimp != IntPtr.Zero && listener != IntPtr.Zero && Native.IsWindow(oldAimp))
                SendAimp(oldAimp, WM_AIMP_COMMAND, AIMP_RA_CMD_UNREGISTER_NOTIFY, listener);

            if (found == IntPtr.Zero)
            {
                bool hadTrack;
                lock (_lock)
                {
                    hadTrack = _current != null;
                    _current = null;
                    _trackKey = "";
                }
                if (hadTrack) Changed?.Invoke();
                return;
            }

            if (listener != IntPtr.Zero)
                SendAimp(found, WM_AIMP_COMMAND, AIMP_RA_CMD_REGISTER_NOTIFY, listener);

            RefreshAll(requestArt: true);
            return;
        }

        if (found != IntPtr.Zero)
            RefreshPlayback(notifyOnlyOnDrift: true);
    }

    public bool HandleWindowMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_AIMP_NOTIFY)
        {
            int notify = wParam.ToInt32();
            if (notify is AIMP_RA_NOTIFY_TRACK_INFO or AIMP_RA_NOTIFY_TRACK_START)
            {
                RefreshAll(requestArt: true);
                return true;
            }
            if (notify == AIMP_RA_NOTIFY_PROPERTY)
            {
                int prop = lParam.ToInt32();
                if (prop is AIMP_RA_PROPERTY_PLAYER_STATE or AIMP_RA_PROPERTY_PLAYER_POSITION
                    or AIMP_RA_PROPERTY_PLAYER_DURATION or AIMP_RA_PROPERTY_TRACK_REPEAT)
                {
                    RefreshPlayback(notifyOnlyOnDrift: false);
                }
                return true;
            }
        }
        else if (msg == WM_COPYDATA && lParam != IntPtr.Zero)
        {
            var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
            if ((uint)cds.dwData.ToInt64() == WM_AIMP_COPYDATA_ALBUMART_ID)
            {
                BitmapImage? art = null;
                if (cds.cbData > 0 && cds.lpData != IntPtr.Zero)
                {
                    var bytes = new byte[cds.cbData];
                    Marshal.Copy(cds.lpData, bytes, 0, cds.cbData);
                    art = DecodeArt(bytes);
                }

                bool updated = false;
                lock (_lock)
                {
                    if (_current is { HasTrack: true })
                    {
                        _current = _current with { Art = art };
                        updated = true;
                    }
                }
                if (updated) Changed?.Invoke();
                return true;
            }
        }
        return false;
    }

    private void RefreshAll(bool requestArt)
    {
        IntPtr aimp, listener;
        lock (_lock)
        {
            aimp = _aimpHwnd;
            listener = _listenerHwnd;
        }
        if (aimp == IntPtr.Zero || !Native.IsWindow(aimp)) return;

        var info = ReadRemoteFileInfo();
        int state = GetProp(aimp, AIMP_RA_PROPERTY_PLAYER_STATE);
        int posMs = GetProp(aimp, AIMP_RA_PROPERTY_PLAYER_POSITION);
        int durMs = GetProp(aimp, AIMP_RA_PROPERTY_PLAYER_DURATION);
        bool repeat = GetProp(aimp, AIMP_RA_PROPERTY_TRACK_REPEAT) != 0;
        bool playing = state == 2;

        if (info == null || !info.Active)
        {
            bool changed;
            lock (_lock)
            {
                changed = _current != null;
                _current = null;
                _trackKey = "";
            }
            if (changed) Changed?.Invoke();
            return;
        }

        double durationMs = durMs > 0 ? durMs : info.Duration;
        var pos = TimeSpan.FromMilliseconds(Math.Max(0, posMs));
        var dur = TimeSpan.FromMilliseconds(Math.Max(0, durationMs));
        var newKey = $"{info.FileName}\n{info.Artist}\n{info.Title}";

        bool trackChanged;
        lock (_lock)
        {
            trackChanged = newKey != _trackKey;
            _trackKey = newKey;
            var art = trackChanged ? null : _current?.Art;
            _current = new AimpTrack(
                HasTrack: info.Title.Length > 0 || info.Artist.Length > 0,
                Title: info.Title,
                Artist: info.Artist,
                Album: info.Album,
                FileName: info.FileName,
                Duration: dur,
                Position: pos,
                PositionUpdatedAt: DateTimeOffset.Now,
                IsPlaying: playing,
                Repeat: repeat,
                Art: art);
        }

        Changed?.Invoke();

        if ((requestArt || trackChanged) && listener != IntPtr.Zero)
        {
            if (!SendAimp(aimp, WM_AIMP_COMMAND, AIMP_RA_CMD_GET_ALBUMART, listener))
            {
                bool hadArt = false;
                lock (_lock)
                {
                    if (_current?.Art != null)
                    {
                        _current = _current with { Art = null };
                        hadArt = true;
                    }
                }
                if (hadArt) Changed?.Invoke();
            }
        }
    }

    private void RefreshPlayback(bool notifyOnlyOnDrift)
    {
        IntPtr aimp;
        lock (_lock) aimp = _aimpHwnd;
        if (aimp == IntPtr.Zero || !Native.IsWindow(aimp)) return;

        int state = GetProp(aimp, AIMP_RA_PROPERTY_PLAYER_STATE);
        int posMs = GetProp(aimp, AIMP_RA_PROPERTY_PLAYER_POSITION);
        int durMs = GetProp(aimp, AIMP_RA_PROPERTY_PLAYER_DURATION);
        bool repeat = GetProp(aimp, AIMP_RA_PROPERTY_TRACK_REPEAT) != 0;
        bool playing = state == 2;
        var pos = TimeSpan.FromMilliseconds(Math.Max(0, posMs));
        var dur = TimeSpan.FromMilliseconds(Math.Max(0, durMs));

        bool notify = false;
        bool needTrackRefresh = false;

        lock (_lock)
        {
            if (_current == null)
            {
                if (playing || state == 1) needTrackRefresh = true;
            }
            else
            {
                var now = DateTimeOffset.Now;
                var expected = _current.Position;
                if (_current.IsPlaying && _current.PositionUpdatedAt != default)
                    expected += now - _current.PositionUpdatedAt;

                bool drifted = (pos - expected).Duration() > TimeSpan.FromMilliseconds(800);
                bool stateChanged = _current.IsPlaying != playing || _current.Repeat != repeat ||
                                    (dur > TimeSpan.Zero && (_current.Duration - dur).Duration() > TimeSpan.FromMilliseconds(500));

                if (!notifyOnlyOnDrift || stateChanged || drifted)
                {
                    _current = _current with
                    {
                        Duration = dur > TimeSpan.Zero ? dur : _current.Duration,
                        Position = pos,
                        PositionUpdatedAt = now,
                        IsPlaying = playing,
                        Repeat = repeat,
                    };
                    notify = true;
                }
            }
        }

        if (needTrackRefresh) RefreshAll(requestArt: true);
        else if (notify) Changed?.Invoke();
    }

    public void PlayPause()
    {
        IntPtr aimp;
        lock (_lock) aimp = _aimpHwnd;
        if (aimp == IntPtr.Zero) return;
        SendAimp(aimp, WM_AIMP_COMMAND, AIMP_RA_CMD_PLAYPAUSE, IntPtr.Zero);
        RefreshPlayback(notifyOnlyOnDrift: false);
    }

    public void Pause()
    {
        IntPtr aimp;
        lock (_lock) aimp = _aimpHwnd;
        if (aimp == IntPtr.Zero) return;
        if (GetProp(aimp, AIMP_RA_PROPERTY_PLAYER_STATE) == 2)
        {
            SendAimp(aimp, WM_AIMP_COMMAND, AIMP_RA_CMD_PAUSE, IntPtr.Zero);
            RefreshPlayback(notifyOnlyOnDrift: false);
        }
    }

    public void Next()
    {
        IntPtr aimp;
        lock (_lock) aimp = _aimpHwnd;
        if (aimp != IntPtr.Zero) SendAimp(aimp, WM_AIMP_COMMAND, AIMP_RA_CMD_NEXT, IntPtr.Zero);
    }

    public void Previous()
    {
        IntPtr aimp;
        lock (_lock) aimp = _aimpHwnd;
        if (aimp != IntPtr.Zero) SendAimp(aimp, WM_AIMP_COMMAND, AIMP_RA_CMD_PREV, IntPtr.Zero);
    }

    public void Seek(TimeSpan position)
    {
        IntPtr aimp;
        lock (_lock) aimp = _aimpHwnd;
        if (aimp == IntPtr.Zero) return;
        var clamped = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        int ms = (int)Math.Clamp(clamped.TotalMilliseconds, 0, int.MaxValue);
        SendMessageTimeout(aimp, WM_AIMP_PROPERTY, (IntPtr)(AIMP_RA_PROPERTY_PLAYER_POSITION | AIMP_RA_PROPVALUE_SET), (IntPtr)ms, SMTO_ABORTIFHUNG, 150, out _);

        bool updated = false;
        lock (_lock)
        {
            if (_current != null)
            {
                _current = _current with { Position = clamped, PositionUpdatedAt = DateTimeOffset.Now };
                updated = true;
            }
        }
        if (updated) Changed?.Invoke();
    }

    public void CycleRepeat()
    {
        IntPtr aimp;
        lock (_lock) aimp = _aimpHwnd;
        if (aimp == IntPtr.Zero) return;
        int next = GetProp(aimp, AIMP_RA_PROPERTY_TRACK_REPEAT) == 0 ? 1 : 0;
        SendMessageTimeout(aimp, WM_AIMP_PROPERTY, (IntPtr)(AIMP_RA_PROPERTY_TRACK_REPEAT | AIMP_RA_PROPVALUE_SET), (IntPtr)next, SMTO_ABORTIFHUNG, 150, out _);
        RefreshPlayback(notifyOnlyOnDrift: false);
    }

    private static int GetProp(IntPtr aimp, int propId)
    {
        if (SendMessageTimeout(aimp, WM_AIMP_PROPERTY, (IntPtr)(propId | AIMP_RA_PROPVALUE_GET), IntPtr.Zero, SMTO_ABORTIFHUNG, 150, out var res) == IntPtr.Zero)
            return 0;
        return res.ToInt32();
    }

    private static bool SendAimp(IntPtr aimp, int msg, int wParam, IntPtr lParam)
    {
        if (SendMessageTimeout(aimp, msg, (IntPtr)wParam, lParam, SMTO_ABORTIFHUNG, 150, out var res) == IntPtr.Zero)
            return false;
        return res != IntPtr.Zero;
    }

    internal sealed record ParsedRemoteInfo(bool Active, string Title, string Artist, string Album, string FileName, uint Duration);

    private static ParsedRemoteInfo? ReadRemoteFileInfo()
    {
        var map = OpenFileMapping(FILE_MAP_READ, false, RemoteClass);
        if (map == IntPtr.Zero) return null;
        try
        {
            var view = MapViewOfFile(map, FILE_MAP_READ, 0, 0, (UIntPtr)MapFileSize);
            if (view == IntPtr.Zero) return null;
            try
            {
                var buf = new byte[MapFileSize];
                Marshal.Copy(view, buf, 0, MapFileSize);
                return ParseRemoteFileInfo(buf);
            }
            finally
            {
                UnmapViewOfFile(view);
            }
        }
        finally
        {
            CloseHandle(map);
        }
    }

    internal static ParsedRemoteInfo? ParseRemoteFileInfo(ReadOnlySpan<byte> buffer)
    {
        int headerSize = Marshal.SizeOf<RemoteFileInfoHeader>();
        if (buffer.Length < headerSize) return null;

        var header = MemoryMarshal.Read<RemoteFileInfoHeader>(buffer);
        if (header.Active == 0)
            return new ParsedRemoteInfo(false, "", "", "", "", 0);

        int offset = headerSize;
        string album = ReadUtf16(buffer, ref offset, header.AlbumLength);
        string artist = ReadUtf16(buffer, ref offset, header.ArtistLength);
        _ = ReadUtf16(buffer, ref offset, header.DateLength);
        string fileName = ReadUtf16(buffer, ref offset, header.FileNameLength);
        _ = ReadUtf16(buffer, ref offset, header.GenreLength);
        string title = ReadUtf16(buffer, ref offset, header.TitleLength);

        if (title.Length == 0 && fileName.Length > 0)
            title = Path.GetFileNameWithoutExtension(fileName);

        return new ParsedRemoteInfo(true, title, artist, album, fileName, header.Duration);
    }

    private static string ReadUtf16(ReadOnlySpan<byte> buffer, ref int offset, uint charCount)
    {
        if (charCount == 0) return "";
        long byteLen = (long)charCount * 2;
        if (byteLen <= 0 || offset + byteLen > buffer.Length) return "";
        var s = Encoding.Unicode.GetString(buffer.Slice(offset, (int)byteLen));
        offset += (int)byteLen;
        return s.TrimEnd('\0');
    }

    private static BitmapImage? DecodeArt(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 400;
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
}

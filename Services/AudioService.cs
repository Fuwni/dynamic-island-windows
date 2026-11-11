using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace DynamicIsland.Services;

/// <summary>
/// System audio level (for the liquid wave) and per-app volume of the current player.
/// Must be used from the UI thread.
/// </summary>
public sealed class AudioService
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private MMDevice? _device;
    private DateTime _deviceAt;

    private MMDevice? Device
    {
        get
        {
            // default device can change (headphones plugged in) - refresh every few seconds
            if (_device == null || (DateTime.Now - _deviceAt).TotalSeconds > 5)
            {
                try
                {
                    _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    _deviceAt = DateTime.Now;
                }
                catch { _device = null; }
            }
            return _device;
        }
    }

    public float Peak
    {
        get
        {
            try { return Device?.AudioMeterInformation.MasterPeakValue ?? 0; }
            catch { _device = null; return 0; }
        }
    }

    /// <summary>Finds the audio session of the player app; null = use master volume.</summary>
    private AudioSessionControl? FindSession(string appId)
    {
        var device = Device;
        if (device == null || string.IsNullOrWhiteSpace(appId)) return null;

        var tokens = Tokens(appId);
        if (tokens.Count == 0) return null;

        // browsers have several sessions: take the one that is actually making sound
        AudioSessionControl? best = null;
        float bestPeak = -1;
        try
        {
            var manager = device.AudioSessionManager;
            manager.RefreshSessions();
            var sessions = manager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                if (s.IsSystemSoundsSession) continue;
                string name;
                try { name = Process.GetProcessById((int)s.GetProcessID).ProcessName.ToLowerInvariant(); }
                catch { continue; }
                if (!tokens.Any(t => name.Contains(t) || t.Contains(name))) continue;

                float peak = 0;
                try { peak = s.AudioMeterInformation.MasterPeakValue; } catch { }
                if (s.State == NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive) peak += 0.001f;
                if (peak > bestPeak) { best = s; bestPeak = peak; }
            }
        }
        catch { }
        return best;
    }

    private static List<string> Tokens(string appId)
    {
        var raw = appId.ToLowerInvariant().Replace(".exe", "");
        return raw.Split(['.', '_', '!', ' ', '-', '\\', '/'], StringSplitOptions.RemoveEmptyEntries)
                  .Where(t => t.Length >= 4 && t != "microsoft" && t != "app" && !t.All(Uri.IsHexDigit))
                  .ToList();
    }

    public (float Volume, bool PerApp) GetVolume(string appId)
    {
        try
        {
            var s = FindSession(appId);
            if (s != null) return (s.SimpleAudioVolume.Volume, true);
            return (Device?.AudioEndpointVolume.MasterVolumeLevelScalar ?? 0, false);
        }
        catch { return (0, false); }
    }

    /// <summary>App's own output: is it there, is it muted, how loud right now.</summary>
    public (bool Found, bool Muted, float Peak) AppState(string app)
    {
        try
        {
            var s = FindSession(app);
            if (s == null) return (false, false, 0);
            return (true, s.SimpleAudioVolume.Mute, s.AudioMeterInformation.MasterPeakValue);
        }
        catch { return (false, false, 0); }
    }

    public bool ToggleAppMute(string app)
    {
        try
        {
            var s = FindSession(app);
            if (s == null) return false;
            s.SimpleAudioVolume.Mute = !s.SimpleAudioVolume.Mute;
            return s.SimpleAudioVolume.Mute;
        }
        catch { return false; }
    }

    private MMDevice? Mic
    {
        get
        {
            try { return _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications); }
            catch { return null; }
        }
    }

    /// <summary>
    /// Processes that have a live microphone stream open right now (any input device).
    /// This is the real signal for "Discord is in a voice channel".
    /// </summary>
    public HashSet<string> AppsRecording()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var dev in _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                try
                {
                    var mgr = dev.AudioSessionManager;
                    mgr.RefreshSessions();
                    var sessions = mgr.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var s = sessions[i];
                        if (s.State != NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive) continue;
                        try { names.Add(Process.GetProcessById((int)s.GetProcessID).ProcessName); } catch { }
                    }
                }
                catch { }
            }
        }
        catch { }
        return names;
    }

    /// <summary>Default microphone mute (affects every app: Discord, Zoom, games…).</summary>
    public bool? MicMuted
    {
        get { try { return Mic?.AudioEndpointVolume.Mute; } catch { return null; } }
        set { try { var m = Mic; if (m != null && value.HasValue) m.AudioEndpointVolume.Mute = value.Value; } catch { } }
    }

    public float MicPeak
    {
        get { try { return Mic?.AudioMeterInformation.MasterPeakValue ?? 0; } catch { return 0; } }
    }

    public void SetVolume(string appId, float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        try
        {
            var s = FindSession(appId);
            if (s != null) s.SimpleAudioVolume.Volume = value;
            else if (Device != null) Device.AudioEndpointVolume.MasterVolumeLevelScalar = value;
        }
        catch { }
    }
}

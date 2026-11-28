using System.IO;
using ScreenRecorderLib;

namespace DynamicIsland.Services;

/// <summary>Records the selected monitor to MP4 (H.264) with system sound and/or microphone.</summary>
public sealed class ScreenRecording
{
    private Recorder? _recorder;

    public bool IsRecording { get; private set; }
    public DateTime StartedAt { get; private set; }

    /// <summary>Path of the finished file, or null + error text.</summary>
    public event Action<string?, string?>? Finished;

    public static string VideosFolder
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            var dir = Path.Combine(root, "Dynamic Island");          // folder from before the rename
            if (!Directory.Exists(dir)) dir = Path.Combine(root, "Atoll");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public void Start(bool systemAudio, bool microphone, string displayDevice = "")
    {
        if (IsRecording) return;

        if (!string.IsNullOrEmpty(displayDevice) && !System.Windows.Forms.Screen.AllScreens.Any(
                s => string.Equals(s.DeviceName, displayDevice, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(L.T("Выбранный экран отключён. Запустите запись снова и выберите другой экран."));

        var display = string.IsNullOrEmpty(displayDevice)
            ? new DisplayRecordingSource(DisplayRecordingSource.MainMonitor)
            : new DisplayRecordingSource(displayDevice);
        var sources = new SourceOptions
        {
            RecordingSources = { display },
        };
        var audio = new AudioOptions { IsAudioEnabled = systemAudio || microphone };
        audio.AudioSources ??= new List<AudioSourceBase>();
        if (systemAudio)
        {
            // what you hear, from the current default output device
            var outs = Recorder.GetSystemAudioLoopbackDevices();
            var dev = outs.FirstOrDefault(d => d.IsDefaultDevice) ?? outs.FirstOrDefault();
            if (dev != null) audio.AudioSources.Add(dev);
        }
        if (microphone)
        {
            var mics = Recorder.GetSystemAudioCaptureDevices();
            var mic = mics.FirstOrDefault(d => d.IsDefaultDevice) ?? mics.FirstOrDefault();
            if (mic != null) audio.AudioSources.Add(mic);
        }
        audio.IsAudioEnabled = audio.AudioSources.Count > 0;

        var options = new RecorderOptions
        {
            SourceOptions = sources,
            AudioOptions = audio,
            VideoEncoderOptions = new VideoEncoderOptions
            {
                Framerate = 60,
                IsFixedFramerate = false,
                IsHardwareEncodingEnabled = true,
                Quality = 80,
                Encoder = new H264VideoEncoder
                {
                    BitrateMode = H264BitrateControlMode.Quality,
                    EncoderProfile = H264Profile.High,
                },
            },
            MouseOptions = new MouseOptions { IsMousePointerEnabled = true },
        };

        _recorder = Recorder.CreateRecorder(options);
        _recorder.OnRecordingComplete += (_, e) => Done(e.FilePath, null);
        _recorder.OnRecordingFailed += (_, e) => Done(null, e.Error);

        var path = Path.Combine(VideosFolder, L.T("Запись экрана") + DateTime.Now.ToString(" yyyy-MM-dd HH-mm-ss") + ".mp4");
        StartedAt = DateTime.Now;
        IsRecording = true;
        try { _recorder.Record(path); }
        catch
        {
            IsRecording = false;
            _recorder.Dispose();
            _recorder = null;
            throw;
        }
    }

    public void Stop()
    {
        if (!IsRecording) return;
        _recorder?.Stop();
    }

    private void Done(string? path, string? error)
    {
        IsRecording = false;
        var r = _recorder;
        _recorder = null;
        Finished?.Invoke(path, error);
        // never dispose the recorder from inside its own callback - it deadlocks
        Task.Run(async () =>
        {
            await Task.Delay(500);
            try { r?.Dispose(); } catch { /* already disposed */ }
        });
    }
}

using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// "--review &lt;work-folder&gt;": the ~90 s narrated tour. Reads segments.json (voice-over durations
/// from tools/review/voice.py), plays one scene per spoken segment on demo data while the GPU films
/// the stage, and writes timeline.json with when each segment really started, so
/// tools/review/compose.py can lay the voice exactly over its scene.
/// </summary>
public partial class MainWindow
{
    private sealed record ReviewSegment(string key, string title, string sub, double duration);

    /// <summary>Start filming the stage window with Windows Graphics Capture (hardware H.264, 60 fps).</summary>
    private async Task<(ScreenRecorderLib.Recorder Rec, Task<string?> Finished)> StartStageRecordingAsync(Window stage, string path)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(stage).Handle;
        var options = new ScreenRecorderLib.RecorderOptions
        {
            SourceOptions = new ScreenRecorderLib.SourceOptions
            {
                RecordingSources = { new ScreenRecorderLib.WindowRecordingSource(hwnd) { IsCursorCaptureEnabled = false } },
            },
            AudioOptions = new ScreenRecorderLib.AudioOptions { IsAudioEnabled = false },
            OutputOptions = new ScreenRecorderLib.OutputOptions { RecorderMode = ScreenRecorderLib.RecorderMode.Video },
            VideoEncoderOptions = new ScreenRecorderLib.VideoEncoderOptions
            {
                Framerate = 60, IsFixedFramerate = true, IsHardwareEncodingEnabled = true, Bitrate = 30_000_000,
                Encoder = new ScreenRecorderLib.H264VideoEncoder { BitrateMode = ScreenRecorderLib.H264BitrateControlMode.CBR, EncoderProfile = ScreenRecorderLib.H264Profile.High },
            },
            MouseOptions = new ScreenRecorderLib.MouseOptions { IsMousePointerEnabled = false },
        };
        var rec = ScreenRecorderLib.Recorder.CreateRecorder(options);
        var started = new TaskCompletionSource();
        var finished = new TaskCompletionSource<string?>();
        rec.OnStatusChanged += (_, e) =>
        {
            if (e.Status != ScreenRecorderLib.RecorderStatus.Recording) return;
            _promoClock.Restart();               // all times are measured from the first recorded frame
            started.TrySetResult();
        };
        rec.OnRecordingComplete += (_, e) => finished.TrySetResult(e.FilePath);
        rec.OnRecordingFailed += (_, e) => { Log.Write("stage recording failed: " + e.Error); started.TrySetResult(); finished.TrySetResult(null); };
        if (File.Exists(path)) File.Delete(path);   // the recorder can't overwrite a file
        rec.Record(path);
        await started.Task;
        return (rec, finished.Task);
    }

    private void UseCharacter(string id)
    {
        _settings.Character = id;
        Character.Kind = id;
        SetupAura();
    }

    private async Task RunReviewAsync(string folder)
    {
        folder = Path.GetFullPath(folder);
        var segments = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "segments.json"))).RootElement
            .GetProperty("segments").EnumerateArray()
            .Select(s => new ReviewSegment(s.GetProperty("key").GetString()!, s.GetProperty("title").GetString()!,
                                           s.GetProperty("sub").GetString()!, s.GetProperty("duration").GetDouble()))
            .Where(s => s.key is not ("intro" or "cta"))     // those are spoken over the animated intro / outro
            .ToList();
        _captions = segments.ToDictionary(s => s.key, s => (s.title, s.sub));

        Island.IsHitTestVisible = false;
        _settings.ExpandOnHover = false;
        _settings.Aura = "none";
        _demoIdle = true;
        UseCharacter("eyes");
        RenderPromoStills(folder);
        await Task.Delay(6000); // weather + layout
        UpdateMediaUi();
        SetMode(Mode.Compact);

        var stage = BuildStageWindow();
        stage.Show();
        System.Windows.Media.CompositionTarget.Rendering += PromoFrame;
        _camTarget = FollowIsland;
        (_camC, _camZ) = FollowIsland();
        await Task.Delay(1000);

        var (rec, finished) = await StartStageRecordingAsync(stage, Path.Combine(folder, "stage.mp4"));
        const double gap = 0.35;
        await Task.Delay(400);   // a breath before the first line

        foreach (var seg in segments)
        {
            double D = seg.duration + gap;
            var start = _promoClock.Elapsed.TotalSeconds;
            Caption(seg.key);
            Task Until(double fraction)
            {
                var wait = start + D * fraction - _promoClock.Elapsed.TotalSeconds;
                return wait > 0 ? Task.Delay(TimeSpan.FromSeconds(wait)) : Task.CompletedTask;
            }

            switch (seg.key)
            {
                case "compact":
                    _cur = Center(Island, 0.85, 1) + new Vector(60, 240);
                    _curTarget = _cur;
                    _cursorOn = true;
                    await Until(0.25);
                    _curTarget = Center(Island, 0.75, 1) + new Vector(0, 24);
                    await Until(0.6);
                    _curTarget = Center(Island, 0.25, 1) + new Vector(0, 22);
                    break;

                case "music":
                    _curTarget = Center(Island, 0.5, 0.5);
                    await Until(0.08);
                    Click();
                    _demoIdle = false;
                    UpdateMediaUi();
                    _pinnedOpen = true;
                    TabMusic.IsChecked = true;
                    Expand();
                    _cursorOn = false;
                    break;

                case "home":
                    TabHome.IsChecked = true;
                    break;

                case "weather":
                    TabWeather.IsChecked = true;
                    break;

                case "live":
                    _pinnedOpen = false;
                    SetMode(Mode.Compact);
                    _timer.SetKind(TimerKind.Timer);
                    _timer.SetDuration(TimeSpan.FromMinutes(25));
                    _timer.Start();
                    await Until(0.5);
                    ShowToast("", "Загрузка завершена", "presentation.pdf", null, (Brush)FindResource("AccentBlue"), ms: (int)(D * 450));
                    await Until(0.97);
                    _timer.Reset();
                    break;

                case "tools":
                    _pinnedOpen = true;
                    TabClip.IsChecked = true;
                    Expand();
                    await Until(0.36);
                    TabNotes.IsChecked = true;
                    await Until(0.68);
                    TabCalendar.IsChecked = true;
                    break;

                case "command":
                    _pinnedOpen = false;
                    CmdBarBox.Text = "";
                    SetMode(Mode.Command);
                    await Until(0.15);
                    foreach (var ch in "таймер 25")
                    {
                        CmdBarBox.Text += ch;
                        CmdBarBox.CaretIndex = CmdBarBox.Text.Length;
                        await Task.Delay(110);
                    }
                    await Until(0.96);
                    CmdBarBox.Text = "";
                    SetMode(Mode.Compact);
                    break;

                case "chars":
                    _demoIdle = true;
                    UpdateMediaUi();
                    SetMode(Mode.Compact);
                    _cursorOn = true;
                    _cur = Center(Island, 0.9, 1) + new Vector(40, 200);
                    _curTarget = Center(Island, 0.8, 1) + new Vector(30, 40);
                    var parade = new[] { "cat", "ghost", "bunny", "robot", "owl", "dino", "planet", "star", "cloud", "claude", "eyes" };
                    for (int i = 0; i < parade.Length; i++)
                    {
                        UseCharacter(parade[i]);
                        Character.Cheer();
                        _curTarget = Center(Island, i % 2 == 0 ? 0.15 : 0.85, 1) + new Vector(0, 40);
                        await Until((i + 1.0) / (parade.Length + 0.5));
                    }
                    break;

                case "emo":
                    _curTarget = Center(Character, 0.6, 0.8);
                    await Until(0.12);
                    var emos = new[] { Views.Emotion.Love, Views.Emotion.Surprised, Views.Emotion.Dizzy, Views.Emotion.Cool, Views.Emotion.Laugh };
                    for (int i = 0; i < emos.Length; i++)
                    {
                        Click();
                        Character.Emote(emos[i]);
                        await Until(0.12 + 0.86 * (i + 1) / emos.Length);
                    }
                    _cursorOn = false;
                    break;

                case "clawd":
                    UseCharacter("clawd");
                    _demoIdle = false;
                    UpdateMediaUi();
                    _pinnedOpen = true;
                    TabNotes.IsChecked = true;
                    Expand();
                    await Until(0.12);
                    _camTarget = () => (Center(MenuBuddy) + new Vector(-12, 6), 6.0);
                    _menuTypedAt = DateTime.Now.AddSeconds(60);
                    await Until(0.8);
                    _menuTypedAt = default;   // closes the lid in time for the next scene
                    break;

                case "look":
                    _camTarget = FollowIsland;
                    TabSettings.IsChecked = true;
                    await Until(0.45);
                    _pinnedOpen = false;
                    SetMode(Mode.Compact);
                    _demoIdle = true;
                    UpdateMediaUi();
                    _settings.Aura = "aurora";
                    _settings.HandDrawn = true;
                    UseCharacter("cat");
                    break;

                case "dev":
                    _settings.Aura = "none";
                    _settings.HandDrawn = false;
                    UseCharacter("clawd");
                    var req = new ClaudeBridge.PermissionRequest { Tool = "Bash", Details = "dotnet build -c Release", Project = "dynamic-island" };
                    EnqueuePermission(req);
                    await Until(0.5);
                    _cursorOn = true;
                    _cur = Center(Island, 0.5, 1) + new Vector(120, 160);
                    _curTarget = Center(ClaudeAllowButton);
                    await Until(0.7);
                    Click();
                    Answer("allow");
                    _cursorOn = false;
                    await Until(0.76);
                    OnClaudeNotice(new ClaudeBridge.Notice("stop", "", "Сборка прошла успешно", "dynamic-island"));
                    break;

                case "new":
                    SetMode(Mode.Compact);
                    await Until(0.12);
                    _pinnedOpen = true;
                    TabHome.IsChecked = true;
                    Expand();                         // the soft blur + colour split + spring
                    await Until(0.55);
                    _pinnedOpen = false;
                    SetMode(Mode.Compact);
                    await Until(0.68);
                    Character.Emote(Views.Emotion.Cool);
                    break;

                case "honest":
                    UseCharacter("eyes");
                    _demoIdle = true;
                    UpdateMediaUi();
                    await Until(0.08);
                    Character.Emote(Views.Emotion.Sad);
                    await Until(0.62);                // "...зато бесплатно"
                    Character.Emote(Views.Emotion.Love);
                    break;

                case "future":
                    UseCharacter("clawd");
                    await Until(0.2);
                    Character.Greet();
                    await Until(0.6);
                    Character.Emote(Views.Emotion.Laugh);
                    break;
            }
            await Until(1);
        }

        double total = _promoClock.Elapsed.TotalSeconds + 0.3;
        await Task.Delay(300);
        rec.Stop();
        var file = await finished;
        System.Windows.Media.CompositionTarget.Rendering -= PromoFrame;
        File.WriteAllText(Path.Combine(folder, "timeline.json"), JsonSerializer.Serialize(new
        {
            total,
            segments = _captionLog.Select(c => new { key = c.Key, t = c.T }),
        }));
        Log.Write($"review: {file ?? "failed"}");
        await Task.Delay(500);
        Application.Current.Shutdown();
    }
}

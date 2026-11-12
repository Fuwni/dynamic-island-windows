using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// "--promo &lt;folder&gt; [ru|en]": films the parts of a vertical 1080×1920 ad on demo data.
/// The live island is drawn through a VisualBrush (vector, sharp at any zoom) on a "wallpaper" stage
/// in a small on-screen window, with a scripted cursor and a moving camera; the GPU films that window.
/// The static background and the captions are rendered to PNGs with their timings, and
/// tools/compose-promo.py puts it all together with ffmpeg. Nothing of the user's is shown.
/// </summary>
public partial class MainWindow
{
    private const int PW = 1080, PH = 1920;
    private static readonly Rect PromoStage = new(40, 640, 1000, 860);

    private Grid? _stageRoot;
    private VisualBrush? _promoBrush;
    private System.Windows.Shapes.Path? _cursorPath;
    private Ellipse? _promoRipple;
    private Point _camC = new(450, 30);
    private double _camZ = 3.2;
    private Func<(Point C, double Z)> _camTarget = () => (new Point(450, 30), 3.2);
    private Point _cur = new(700, 700), _curTarget = new(700, 700);
    private bool _cursorOn;
    private double _rippleSince = -10, _lastFrameT;
    private readonly System.Diagnostics.Stopwatch _promoClock = new();
    private readonly List<(string Key, double T)> _captionLog = [];
    private Dictionary<string, (string Title, string Sub)> _captions = [];

    private static Dictionary<string, (string, string)> PromoCaptions(string lang) => lang == "en"
        ? new()
        {
            ["intro"] = ("Dynamic Island", "now on Windows"),
            ["reach"] = ("It feels alive", "it reaches for your cursor"),
            ["music"] = ("Music with live lyrics", "works with any player"),
            ["home"] = ("Everything one click away", "screenshots, OCR, recording, QR"),
            ["weather"] = ("Weather like on iPhone", "wind, UV, sunrise, hourly"),
            ["live"] = ("Live Activities", "timers, downloads, notifications"),
            ["chars"] = ("15 living characters", "they watch your cursor and dance"),
            ["emo"] = ("Tap one", "7 hidden emotions"),
            ["clawd"] = ("Claude Code types with you", "AI chat built right in"),
            ["end"] = ("Free & open source", "link in bio"),
        }
        : new()
        {
            ["intro"] = ("Dynamic Island", "теперь на Windows"),
            ["reach"] = ("Он живой", "тянется к курсору"),
            ["music"] = ("Музыка со словами песен", "любой плеер: Spotify, YouTube, Яндекс"),
            ["home"] = ("Всё под рукой", "скриншот, текст с экрана, запись, QR"),
            ["weather"] = ("Погода как на iPhone", "ветер, УФ, восход, прогноз по часам"),
            ["live"] = ("Живые активности", "таймеры, загрузки, уведомления"),
            ["chars"] = ("15 живых персонажей", "следят за курсором и танцуют"),
            ["emo"] = ("Нажми на него", "7 эмоций-пасхалок"),
            ["clawd"] = ("Claude Code печатает с тобой", "чат с ИИ прямо в острове"),
            ["end"] = ("Бесплатно и с открытым кодом", "ссылка в профиле"),
        };

    // ------------------------------------------------------------------ static pictures

    private static readonly FontFamily DisplayFont = new("Segoe UI Variable Display, Segoe UI");

    private static void SavePng(FrameworkElement e, int w, int h, string path)
    {
        e.Measure(new Size(w, h));
        e.Arrange(new Rect(0, 0, w, h));
        e.UpdateLayout();
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(e);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    private void RenderPromoStills(string folder)
    {
        // background: dark, two soft glows, a brand line, the stage frame and the link
        var bg = new Canvas { Width = PW, Height = PH };
        bg.Children.Add(new Rectangle
        {
            Width = PW, Height = PH,
            Fill = new LinearGradientBrush(Color.FromRgb(0x0A, 0x0A, 0x10), Color.FromRgb(0x17, 0x10, 0x1E), 90),
        });
        void Glow(Color c, double x, double y, double r)
        {
            var e = new Ellipse
            {
                Width = r * 2, Height = r * 2,
                Fill = new RadialGradientBrush(Color.FromArgb(0x70, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B)),
            };
            Canvas.SetLeft(e, x - r);
            Canvas.SetTop(e, y - r);
            bg.Children.Add(e);
        }
        Glow(Color.FromRgb(0xD9, 0x77, 0x57), 260, 300, 560);
        Glow(Color.FromRgb(0x5E, 0x5C, 0xE6), 900, 1750, 600);
        var brand = new TextBlock
        {
            Text = "DYNAMIC ISLAND  ·  WINDOWS", FontSize = 28, FontWeight = FontWeights.SemiBold, FontFamily = DisplayFont,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF0, 0xA2, 0x7A)), Width = PW, TextAlignment = TextAlignment.Center,
        };
        Canvas.SetTop(brand, 196);
        bg.Children.Add(brand);
        var frame = new Rectangle
        {
            Width = PromoStage.Width + 6, Height = PromoStage.Height + 6, RadiusX = 49, RadiusY = 49,
            Stroke = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)), StrokeThickness = 3,
        };
        Canvas.SetLeft(frame, PromoStage.X - 3);
        Canvas.SetTop(frame, PromoStage.Y - 3);
        bg.Children.Add(frame);
        var link = new TextBlock
        {
            Text = "github.com/Fuwni/dynamic-island-windows", FontSize = 28, FontFamily = new FontFamily("Segoe UI"),
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x9A)), Width = PW, TextAlignment = TextAlignment.Center,
        };
        Canvas.SetTop(link, PromoStage.Bottom + 34);
        bg.Children.Add(link);
        SavePng(bg, PW, PH, System.IO.Path.Combine(folder, "bg.png"));

        // rounded mask for the stage video
        var mask = new Grid { Width = PromoStage.Width, Height = PromoStage.Height, Background = Brushes.Black };
        mask.Children.Add(new Rectangle { Fill = Brushes.White, RadiusX = 46, RadiusY = 46 });
        SavePng(mask, (int)PromoStage.Width, (int)PromoStage.Height, System.IO.Path.Combine(folder, "mask.png"));

        // one transparent picture per caption
        foreach (var (key, (title, sub)) in _captions)
        {
            var panel = new StackPanel { Width = PW, Margin = new Thickness(0, 10, 0, 0) };
            panel.Children.Add(new TextBlock
            {
                Text = title, FontSize = 84, FontWeight = FontWeights.Bold, Foreground = Brushes.White, FontFamily = DisplayFont,
                TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(60, 0, 60, 0), LineHeight = 94,
            });
            panel.Children.Add(new TextBlock
            {
                Text = sub, FontSize = 44, Foreground = new SolidColorBrush(Color.FromRgb(0xB9, 0xB9, 0xC8)),
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(60, 16, 60, 0),
            });
            var host = new Grid { Width = PW, Height = 330, Background = Brushes.Transparent };
            host.Children.Add(panel);
            SavePng(host, PW, 330, System.IO.Path.Combine(folder, $"cap_{key}.png"));
        }
    }

    // ------------------------------------------------------------------ the stage (filmed live)

    private Window BuildStageWindow()
    {
        var wall = new Grid { Width = PromoStage.Width, Height = PromoStage.Height, ClipToBounds = true };
        wall.Children.Add(new Rectangle
        {
            Fill = new LinearGradientBrush(Color.FromRgb(0x1B, 0x25, 0x5C), Color.FromRgb(0x4B, 0x1D, 0x5E), 35),
        });
        void Blob(Color c, double x, double y, double r) => wall.Children.Add(new Ellipse
        {
            Width = r * 2, Height = r * 2, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(x - r, y - r, 0, 0),
            Fill = new RadialGradientBrush(Color.FromArgb(0xC0, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B)),
        });
        Blob(Color.FromRgb(0xFF, 0x6B, 0x9A), 820, 760, 520);
        Blob(Color.FromRgb(0x3A, 0x8D, 0xFF), 120, 820, 480);
        Blob(Color.FromRgb(0xFF, 0xA8, 0x5C), 600, 1000, 420);
        Blob(Color.FromRgb(0x8E, 0x5C, 0xFF), 300, 300, 380);
        _promoBrush = new VisualBrush(Root)
        {
            Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top,
        };
        wall.Children.Add(new Rectangle { Fill = _promoBrush });

        var overlay = new Canvas();
        _promoRipple = new Ellipse { Stroke = Brushes.White, StrokeThickness = 4, Opacity = 0 };
        overlay.Children.Add(_promoRipple);
        _cursorPath = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M0,0 L0,17 L4.5,13 L7.5,20 L10,19 L7,12.5 L13,12.5 Z"),
            Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1.1, StrokeLineJoin = PenLineJoin.Round,
            RenderTransform = new ScaleTransform(2.6, 2.6), Opacity = 0,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.5 },
        };
        overlay.Children.Add(_cursorPath);
        wall.Children.Add(overlay);
        _stageRoot = wall;

        return new Window
        {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
            Topmost = true, Width = PromoStage.Width, Height = PromoStage.Height,
            Left = (SystemParameters.PrimaryScreenWidth - PromoStage.Width) / 2, Top = 140,
            Content = wall, Background = Brushes.Black, Title = "Dynamic Island promo",
        };
    }

    private void Caption(string key) => _captionLog.Add((key, _promoClock.Elapsed.TotalSeconds));

    private Point Center(FrameworkElement e, double fx = 0.5, double fy = 0.5)
    {
        try { return e.TransformToAncestor(Root).Transform(new Point(e.ActualWidth * fx, e.ActualHeight * fy)); }
        catch { return new Point(450, 30); }
    }

    /// <summary>Camera that frames the whole island, zooming in on the small pill.</summary>
    private (Point, double) FollowIsland()
    {
        var c = Center(Island);
        double z = Math.Min(3.4, Math.Min(PromoStage.Width * 0.84 / Math.Max(1, Island.ActualWidth),
                                          PromoStage.Height * 0.8 / Math.Max(1, Island.ActualHeight)));
        return (new Point(c.X, c.Y + 8 / z), z);
    }

    private void Click() => _rippleSince = _promoClock.Elapsed.TotalSeconds;

    private void PromoFrame(object? sender, EventArgs e)
    {
        if (_stageRoot == null) return;
        double t = _promoClock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(t - _lastFrameT, 0, 0.1);
        _lastFrameT = t;

        var (tc, tz) = _camTarget();
        double k = Math.Min(1, dt * 4.5);
        _camC = new Point(_camC.X + (tc.X - _camC.X) * k, _camC.Y + (tc.Y - _camC.Y) * k);
        _camZ = Math.Exp(Math.Log(_camZ) + (Math.Log(tz) - Math.Log(_camZ)) * k);
        double vw = PromoStage.Width / _camZ, vh = PromoStage.Height / _camZ;
        var vb = new Rect(_camC.X - vw / 2, _camC.Y - vh * 0.42, vw, vh);
        _promoBrush!.Viewbox = vb;
        Point ToStage(Point p) => new((p.X - vb.X) * _camZ, (p.Y - vb.Y) * _camZ);

        // the cursor also drives the island's eyes and reach through the fake cursor
        double kc = Math.Min(1, dt * 6);
        _cur = new Point(_cur.X + (_curTarget.X - _cur.X) * kc, _cur.Y + (_curTarget.Y - _cur.Y) * kc);
        try
        {
            var s = Root.PointToScreen(_cur);
            _fakeCursor = _cursorOn ? new Native.POINT { X = (int)s.X, Y = (int)s.Y } : new Native.POINT { X = -20000, Y = -20000 };
        }
        catch { }
        var cf = ToStage(_cur);
        _cursorPath!.Opacity += ((_cursorOn ? 1 : 0) - _cursorPath.Opacity) * Math.Min(1, dt * 8);
        Canvas.SetLeft(_cursorPath, cf.X);
        Canvas.SetTop(_cursorPath, cf.Y);
        double rs = t - _rippleSince;
        _promoRipple!.Opacity = rs < 0.45 ? 0.9 * (1 - rs / 0.45) : 0;
        double rr = 14 + rs * 110;
        _promoRipple.Width = _promoRipple.Height = rr * 2;
        Canvas.SetLeft(_promoRipple, cf.X - rr);
        Canvas.SetTop(_promoRipple, cf.Y - rr);
    }

    // ------------------------------------------------------------------ script

    private async Task RunPromoAsync(string folder, string lang)
    {
        folder = System.IO.Path.GetFullPath(folder);
        Directory.CreateDirectory(folder);
        _captions = PromoCaptions(lang);
        Island.IsHitTestVisible = false;
        _settings.ExpandOnHover = false;
        _settings.Character = "eyes";
        Character.Kind = "eyes";
        SetupAura();
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
            _promoClock.Restart();               // caption times are measured from the first recorded frame
            started.TrySetResult();
        };
        rec.OnRecordingComplete += (_, e) => finished.TrySetResult(e.FilePath);
        rec.OnRecordingFailed += (_, e) => { Log.Write("promo failed: " + e.Error); started.TrySetResult(); finished.TrySetResult(null); };
        var stagePath = System.IO.Path.Combine(folder, "stage.mp4");
        if (File.Exists(stagePath)) File.Delete(stagePath);   // the recorder can't overwrite a file
        rec.Record(stagePath);
        await started.Task;
        Task W(double s) => Task.Delay(TimeSpan.FromSeconds(s));

        Caption("intro");
        await W(1.8);

        // 1. the cursor comes in and the pill reaches for it
        Caption("reach");
        _cur = Center(Island, 0.75, 1) + new Vector(80, 260);
        _curTarget = _cur;
        _cursorOn = true;
        _curTarget = Center(Island, 0.7, 1) + new Vector(10, 26);
        await W(1.7);
        _curTarget = Center(Island, 0.3, 1) + new Vector(0, 22);
        await W(1.1);

        // 2. click: open on the music page with lyrics
        _curTarget = Center(Island, 0.5, 0.5);
        await W(0.5);
        Click();
        _pinnedOpen = true;
        TabMusic.IsChecked = true;
        Expand();
        Caption("music");
        _cursorOn = false;
        await W(3.0);

        // 3. home: quick actions
        TabHome.IsChecked = true;
        Caption("home");
        await W(2.5);

        // 4. weather
        TabWeather.IsChecked = true;
        Caption("weather");
        await W(2.5);

        // 5. back to the pill: a timer, then a notification
        _pinnedOpen = false;
        SetMode(Mode.Compact);
        Caption("live");
        _timer.SetKind(TimerKind.Timer);
        _timer.SetDuration(TimeSpan.FromMinutes(25));
        _timer.Start();
        await W(1.9);
        ShowToast("", lang == "en" ? "Download complete" : "Загрузка завершена", "presentation.pdf", null, (Brush)FindResource("AccentBlue"), ms: 1800);
        await W(2.0);
        _timer.Reset();

        // 6. a parade of characters, then poke one for emotions
        _demoIdle = true;
        UpdateMediaUi();
        SetMode(Mode.Compact);
        Caption("chars");
        _cursorOn = true;
        _cur = Center(Island, 0.8, 1) + new Vector(40, 200);
        _curTarget = Center(Island, 0.75, 1) + new Vector(30, 40);
        foreach (var id in new[] { "cat", "ghost", "bunny", "robot", "owl", "dino", "planet", "claude", "clawd", "eyes" })
        {
            Character.Kind = id;
            _settings.Character = id;
            SetupAura();
            Character.Cheer();
            await W(0.42);
        }
        Caption("emo");
        _curTarget = Center(Character, 0.6, 0.8);
        await W(0.6);
        foreach (var emo in new[] { Views.Emotion.Love, Views.Emotion.Surprised, Views.Emotion.Dizzy, Views.Emotion.Cool, Views.Emotion.Laugh })
        {
            Click();
            Character.Emote(emo);
            await W(0.95);
        }
        _cursorOn = false;

        // 7. Claude Code with its laptop, in the open menu
        _settings.Character = "clawd";
        Character.Kind = "clawd";
        SetupAura();
        _demoIdle = false;
        UpdateMediaUi();
        _pinnedOpen = true;
        TabNotes.IsChecked = true;
        Expand();
        Caption("clawd");
        await W(0.7);
        _camTarget = () => (Center(MenuBuddy) + new Vector(-12, 6), 6.0);
        _menuTypedAt = DateTime.Now.AddSeconds(30);
        await W(3.4);
        _menuTypedAt = default;
        await W(1.3);

        // 8. end card: back to the pill, it says hi
        _camTarget = FollowIsland;
        _pinnedOpen = false;
        SetMode(Mode.Compact);
        _demoIdle = true;
        UpdateMediaUi();
        Caption("end");
        await W(0.6);
        Character.Greet();
        await W(1.8);
        Character.Emote(Views.Emotion.Love);
        await W(1.9);

        double total = _promoClock.Elapsed.TotalSeconds;
        rec.Stop();
        var file = await finished.Task;
        System.Windows.Media.CompositionTarget.Rendering -= PromoFrame;
        File.WriteAllText(System.IO.Path.Combine(folder, "captions.json"), JsonSerializer.Serialize(new
        {
            total,
            captions = _captionLog.Select(c => new { key = c.Key, t = c.T }),
        }));
        Log.Write($"promo: {file ?? "failed"}");
        await Task.Delay(500);
        Application.Current.Shutdown();
    }
}

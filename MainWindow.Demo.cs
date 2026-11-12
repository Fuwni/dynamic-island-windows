using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// "--snapshot &lt;folder&gt;": renders every tab on made-up demo data to transparent PNGs (for the README).
/// Nothing of the user's (music, clipboard, notes, settings) is shown or saved.
/// </summary>
public partial class MainWindow
{
    private MediaSnapshot? _demoMedia;
    private bool _demoIdle; // pretend nothing plays (shows the eyes)

    private static readonly Lyrics DemoLyrics = new(
    [
        new LyricLine(TimeSpan.FromSeconds(60), D("Город спит, а мы опять не спим", "The city sleeps, but we're awake again")),
        new LyricLine(TimeSpan.FromSeconds(66), D("Свет фонарей рисует наши тени", "Streetlights draw our shadows on the wall")),
        new LyricLine(TimeSpan.FromSeconds(72), D("Этой ночью всё неважно, кроме нас", "Tonight nothing matters but the two of us")),
        new LyricLine(TimeSpan.FromSeconds(80), D("И музыка, что тихо в окна льётся", "And music softly pouring through the windows")),
        new LyricLine(TimeSpan.FromSeconds(86), D("Пусть утро подождёт ещё немного", "Let the morning wait a little longer")),
    ], true);

    /// <summary>Demo data in Russian, or in English for every other interface language.</summary>
    private static string D(string ru, string en) => Loc.Lang == "ru" ? ru : en;

    private void BeginDemo()
    {
        Settings.ReadOnly = true;
        _settings.Hidden = false;
        _settings.DarkTheme = true;
        _settings.LiquidWaves = false;
        _fakeCursor = new Native.POINT { X = -20000, Y = -20000 }; // ignore the real mouse while shooting
        _settings.Glass = false; // the frost would show the real desktop: keep screenshots clean
        _settings.Docked = Environment.GetCommandLineArgs().Contains("--docked");
        _settings.City = "London";
        _settings.Reminders =
        [
            new Reminder { Title = D("Созвон с командой", "Team call"), When = DateTime.Today.AddHours(DateTime.Now.Hour + 1).AddMinutes(30) },
            new Reminder { Title = D("Забрать посылку", "Pick up the parcel"), When = DateTime.Today.AddDays(1).AddHours(18) },
            new Reminder { Title = D("Тренировка", "Workout"), When = DateTime.Today.AddDays(2).AddHours(9) },
        ];
        NotesStore.Notes.Clear();
        NotesStore.Notes.AddRange(
        [
            new Note { Text = D("Идея: тёмная тема для сайта + плавные переходы", "Idea: dark theme for the site + smooth transitions"), Created = DateTime.Now.AddMinutes(-20) },
            new Note { Text = D("Купить: молоко, кофе, батарейки AA", "Buy: milk, coffee, AA batteries"), Created = DateTime.Now.AddHours(-3) },
            new Note { Text = D("Перезвонить в сервис насчёт ноутбука", "Call the repair shop about the laptop"), Created = DateTime.Now.AddDays(-1) },
        ]);
        _clips.Clear();
        _clips.AddRange(
        [
            new ClipItem("https://github.com/", null, null, DateTime.Now.AddMinutes(-1)),
            new ClipItem(D("Встречаемся в 19:00 у входа в парк", "Meet at 7 pm at the park entrance"), null, null, DateTime.Now.AddMinutes(-6)),
            new ClipItem("dotnet publish -c Release -r win-x64", null, null, DateTime.Now.AddMinutes(-15)),
        ]);
        Theme.Apply(true, "#000000");
        _demoMedia = new MediaSnapshot(D("Ночной город", "Night City"), "Demo Artist", "Spotify", "Spotify", DemoArt(), true,
            TimeSpan.FromSeconds(73), DateTimeOffset.Now, TimeSpan.FromSeconds(214));
    }

    private static BitmapImage DemoArt()
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var bg = new LinearGradientBrush(Color.FromRgb(0xFF, 0x5E, 0x8A), Color.FromRgb(0x5E, 0x5C, 0xE6), 45);
            dc.DrawRectangle(bg, null, new Rect(0, 0, 200, 200));
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)), null, new Point(130, 70), 46, 46);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x33, 0x00, 0x00, 0x00)), null, new Point(60, 150), 70, 70);
        }
        var rtb = new RenderTargetBitmap(200, 200, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        var ms = new MemoryStream();
        enc.Save(ms);
        ms.Position = 0;
        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.StreamSource = ms;
        img.EndInit();
        img.Freeze();
        return img;
    }

    private async Task RunSnapshotsAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        Island.IsHitTestVisible = false; // the real mouse must not open / move it while shooting
        _settings.ExpandOnHover = false;
        await Task.Delay(6000); // weather + layout
        UpdateMediaUi();

        // compact: idle, with the eyes
        _demoIdle = true;
        UpdateMediaUi();
        SetMode(Mode.Compact);
        await Task.Delay(1500);
        Snap(folder, "compact-idle");

        // every character, idle in the pill
        foreach (var (id, _) in Views.CharacterView.All)
        {
            Character.Kind = id;
            _settings.Character = id;
            SetupAura();
            await Task.Delay(900);
            Snap(folder, "character-" + id);
            if (id is "claude" or "clawd")
            {
                _settings.HandDrawn = true;
                await Task.Delay(500);
                Snap(folder, "character-" + id + "-sketch");
                _settings.HandDrawn = false;
            }
        }
        // the easter egg: all seven emotions, on the eyes and on Claude Code
        foreach (var kind in new[] { "eyes", "clawd" })
        {
            Character.Kind = kind;
            foreach (var emo in Enum.GetValues<Views.Emotion>().Skip(1))
            {
                Character.Emote(emo);
                await Task.Delay(650);
                Snap(folder, $"emote-{kind}-{emo.ToString().ToLowerInvariant()}");
            }
            await Task.Delay(2500);
        }
        Character.Kind = _settings.Character;
        _demoIdle = false;
        UpdateMediaUi();

        // the pill reaching for a cursor just below it
        Island.UpdateLayout();
        var tl = Island.PointToScreen(new Point(Island.ActualWidth * 0.7, Island.ActualHeight + 30 / 1.0));
        _fakeCursor = new Native.POINT { X = (int)tl.X, Y = (int)tl.Y };
        await Task.Delay(1200);
        SnapLive(folder, "reach");
        _fakeCursor = new Native.POINT { X = -20000, Y = -20000 };
        await Task.Delay(600);

        // compact: music playing
        SetMode(Mode.Compact);
        await Task.Delay(1200);
        Snap(folder, "compact-music");

        // compact: a timer live activity
        _timer.SetKind(TimerKind.Timer);
        _timer.SetDuration(TimeSpan.FromMinutes(25));
        _timer.Start();
        await Task.Delay(2500);
        Snap(folder, "compact-timer");
        _timer.Reset();

        // toast
        ShowToast("", "Загрузка завершена", "presentation.pdf", null, (Brush)FindResource("AccentBlue"), ms: 60000);
        await Task.Delay(1200);
        Snap(folder, "toast");

        // command bar
        CmdBarBox.Text = "таймер 25";
        SetMode(Mode.Command);
        await Task.Delay(1400);
        Snap(folder, "command");
        CmdBarBox.Text = "";
        SetMode(Mode.Compact);
        await Task.Delay(600);

        // the colour split, caught mid-way through opening
        TabHome.IsChecked = true;
        Expand();
        await Task.Delay(55);
        SnapLive(folder, "open-split");
        await Task.Delay(1200);
        SetMode(Mode.Compact);
        await Task.Delay(700);

        _pinnedOpen = true;
        var tabs = new (RadioButton Tab, string Name)[]
        {
            (TabHome, "home"), (TabMusic, "music"), (TabWeather, "weather"), (TabTimer, "timer"),
            (TabClip, "clipboard"), (TabNotes, "notes"), (TabCalendar, "calendar"), (TabSystem, "system"), (TabSettings, "settings"),
        };
        foreach (var (tab, name) in tabs)
        {
            tab.IsChecked = true;
            if (_mode != Mode.Expanded) Expand();
            if (tab == TabTimer) { _timer.SetKind(TimerKind.Pomodoro); _timer.Start(); RenderTimer(); }
            await Task.Delay(name == "system" ? 10000 : 1600); // perf counters need a few seconds
            Snap(folder, name);
            if (tab == TabTimer) { _timer.Reset(); _timer.SetKind(TimerKind.Timer); }
        }
        // Claude Code typing over the chat window
        _settings.Character = "clawd";
        SetupAura();
        TabChat.IsChecked = true;
        await Task.Delay(2500);
        _chatTypedAt = DateTime.Now.AddSeconds(30); // pretend you keep typing
        await Task.Delay(900);
        SnapLive(folder, "chat-buddy");
        _settings.HandDrawn = true;            // the same, drawn by hand
        await Task.Delay(700);
        SnapLive(folder, "chat-buddy-sketch");
        _settings.HandDrawn = false;
        _chatTypedAt = default;

        // text tools section of the clipboard tab
        TabClip.IsChecked = true;
        TextToolsBox.Text = "Ghbdtn! 'nj ntrcn? yf,hfyysq yt d njq hfcrkflrt";
        ClipSegText.IsChecked = true;
        await Task.Delay(1600);
        Snap(folder, "text-tools");

        Application.Current.Shutdown();
    }

    /// <summary>Like Snap, but keeps the current (moving) shape instead of settling it.</summary>
    private void SnapLive(string folder, string name)
    {
        const double scale = 2;
        UpdateLayout();
        var rtb = new RenderTargetBitmap((int)(Root.ActualWidth * scale), (int)(Root.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(Root);
        var bounds = Island.TransformToAncestor(Root).TransformBounds(new Rect(Island.RenderSize));
        bounds = new Rect(bounds.X - 28, Math.Max(0, bounds.Y - 6), bounds.Width + 56, bounds.Height + 6 + 46);
        bounds.Intersect(new Rect(0, 0, Root.ActualWidth, Root.ActualHeight));
        var px = new Int32Rect((int)(bounds.X * scale), (int)(bounds.Y * scale), (int)(bounds.Width * scale), (int)(bounds.Height * scale));
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(new CroppedBitmap(rtb, px)));
        using var fs = File.Create(Path.Combine(folder, name + ".png"));
        enc.Save(fs);
    }

    /// <summary>Renders the island (with its shadow) at 2× to a transparent PNG.</summary>
    private void Snap(string folder, string name)
    {
        // freeze the morph at its final size so nothing is cut off
        _w.Snap(_w.Target);
        _h.Snap(_h.Target);
        _y.Snap(0);
        Island.Width = _w.Value;
        Island.Height = _h.Value;
        UpdateLayout();
        UpdateShape(false);
        UpdateLayout();
        const double scale = 2;
        var rtb = new RenderTargetBitmap((int)(Root.ActualWidth * scale), (int)(Root.ActualHeight * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(Root);

        var bounds = Island.TransformToAncestor(Root).TransformBounds(new Rect(Island.RenderSize));
        bounds = new Rect(bounds.X - 28, Math.Max(0, bounds.Y - 6), bounds.Width + 56, bounds.Height + 6 + 40);
        bounds.Intersect(new Rect(0, 0, Root.ActualWidth, Root.ActualHeight));
        var px = new Int32Rect((int)(bounds.X * scale), (int)(bounds.Y * scale), (int)(bounds.Width * scale), (int)(bounds.Height * scale));

        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(new CroppedBitmap(rtb, px)));
        using var fs = File.Create(Path.Combine(folder, name + ".png"));
        enc.Save(fs);
    }
}

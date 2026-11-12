using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Services;
using DynamicIsland.Views;

namespace DynamicIsland;

/// <summary>Home tab: quick actions (screenshots, OCR, QR, colour picker, clean text, recording, command).</summary>
public partial class MainWindow
{
    // colour of each action's icon chip (iOS Control Centre style)
    private static readonly Dictionary<string, (string Top, string Bottom)> ActionColors = new()
    {
        ["region"] = ("#3A9BFF", "#0A6CE0"), ["full"] = ("#7B79F7", "#4B49C9"), ["ocr"] = ("#FFB340", "#F08A00"),
        ["qr"] = ("#4AE06F", "#22B348"), ["picker"] = ("#FF5C7F", "#E0264F"), ["cleantext"] = ("#6FDBFF", "#2BA6D6"),
        ["record"] = ("#FF6B61", "#E0352B"), ["command"] = ("#A0A0A8", "#6C6C74"),
    };

    private static Brush ChipBrush(string id)
    {
        var (top, bottom) = ActionColors.GetValueOrDefault(id, ("#3A9BFF", "#0A6CE0"));
        var b = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(top), (Color)ColorConverter.ConvertFromString(bottom), 90);
        b.Freeze();
        return b;
    }

    public static readonly (string Id, string Glyph, string Label)[] AllActions =
    [
        ("region", "", "Область"),
        ("full", "", "Весь экран"),
        ("ocr", "", "Текст с экрана"),
        ("qr", "", "QR-код"),
        ("picker", "", "Пипетка"),
        ("cleantext", "", "Чистый текст"),
        ("record", "", "Запись экрана"),
        ("command", "", "Команда"),
    ];

    private readonly ScreenRecording _recording = new();
    private bool _busy;

    private void InitHome() => BuildHome();

    // three big ones get a wide tile, the rest are round buttons - like Control Centre
    private static readonly HashSet<string> WideActions = ["region", "full", "ocr"];

    private static Brush TintBrush(string hex, byte alpha = 0x2E)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    private static Brush TintText(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    private void BuildHome()
    {
        ActionsGrid.Children.Clear();
        var chosen = AllActions.Where(a => _settings.Actions.Contains(a.Id)).ToList();
        var wide = chosen.Where(a => WideActions.Contains(a.Id)).ToList();
        var round = chosen.Where(a => !WideActions.Contains(a.Id)).ToList();
        double wideWidth = wide.Count == 0 ? 0 : Math.Floor((568 - 8.0 * wide.Count) / wide.Count);
        double roundWidth = round.Count == 0 ? 0 : Math.Floor(Math.Min(113, 566.0 / Math.Max(5, round.Count)));

        foreach (var (id, glyph, label) in wide.Concat(round))
        {
            bool rec = id == "record" && _recording.IsRecording;
            var (top, _) = ActionColors.GetValueOrDefault(id, ("#3A9BFF", "#0A6CE0"));
            var b = new Button
            {
                Style = (Style)FindResource(WideActions.Contains(id) ? "WideAction" : "RoundAction"),
                Tag = rec ? "\uE71A" : glyph,
                Content = rec ? "Стоп" : label,
                Width = WideActions.Contains(id) ? wideWidth : roundWidth,
            };
            if (WideActions.Contains(id)) b.Background = ChipBrush(id);
            else
            {
                // round: coloured line icon on a quiet circle; recording fills it red
                b.Foreground = TintText(top);
                b.Background = rec ? TintBrush("#FF453A", 0xFF) : TintBrush(top, 0x22);
                if (rec) b.Foreground = Brushes.White;
            }
            b.Click += (_, _) => RunAction(id);
            ActionsGrid.Children.Add(b);
        }

        bool actions = _settings.HomeActions && ActionsGrid.Children.Count > 0;
        ActionsGrid.Visibility = actions ? Visibility.Visible : Visibility.Collapsed;
        HomeMusic.Visibility = _settings.HomeMusic ? Visibility.Visible : Visibility.Collapsed;
        HomeWeather.Visibility = _settings.HomeWeather ? Visibility.Visible : Visibility.Collapsed;
        HomeStrips.Visibility = _settings.HomeMusic || _settings.HomeWeather ? Visibility.Visible : Visibility.Collapsed;
        HomeStrips.Margin = new Thickness(4, actions ? 6 : 0, 4, 0);

        // weather alone stretches over the whole row
        if (!_settings.HomeMusic)
        {
            Grid.SetColumn(HomeWeather, 0);
            Grid.SetColumnSpan(HomeWeather, 2);
            HomeWeather.Width = double.NaN;
            HomeWeather.Margin = new Thickness(0);
        }
        else
        {
            Grid.SetColumn(HomeWeather, 1);
            Grid.SetColumnSpan(HomeWeather, 1);
            HomeWeather.Width = 176;
            HomeWeather.Margin = new Thickness(8, 0, 0, 0);
        }

        if (!actions && !_settings.HomeMusic && !_settings.HomeWeather)
        {
            // never leave Home empty
            HomeStrips.Visibility = Visibility.Visible;
            HomeMusic.Visibility = Visibility.Visible;
        }
        Remeasure();
    }

    public async void RunAction(string id)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            switch (id)
            {
                case "region": await RegionScreenshotAsync(); break;
                case "full": await FullScreenshotAsync(); break;
                case "ocr": await OcrFromScreenAsync(); break;
                case "qr": await QrFromScreenAsync(); break;
                case "picker": await PickColorAsync(); break;
                case "cleantext": CleanClipboard(); break;
                case "record": ToggleRecording(); break;
                case "command": RunCommand(); break;
            }
        }
        catch (Exception ex)
        {
            Log.Write($"action {id}: {ex}");
            ShowToast("", "Не получилось", ex.Message);
        }
        finally { _busy = false; }
    }

    /// <summary>Take a picture of the desktop without the island in it.</summary>
    private async Task<(BitmapSource Image, System.Drawing.Rectangle Bounds)> CaptureWithoutIslandAsync()
    {
        if (_mode != Mode.Compact) SetMode(Mode.Compact);
        Root.BeginAnimation(OpacityProperty, null);
        Root.Opacity = 0;
        await Task.Delay(160);
        try { return CaptureService.CaptureDesktop(); }
        finally { if (!_fullscreenHidden) Root.Opacity = 1; }
    }

    private async Task<BitmapSource?> PickRegionAsync()
    {
        var (desktop, _) = await CaptureWithoutIslandAsync();
        var picked = await RegionPicker.PickRegionAsync(desktop);
        return picked?.Image;
    }

    private async Task RegionScreenshotAsync()
    {
        var img = await PickRegionAsync();
        if (img != null) new ScreenshotEditor(img).Show();
    }

    private async Task FullScreenshotAsync()
    {
        if (_mode != Mode.Compact) SetMode(Mode.Compact);
        Root.Opacity = 0;
        await Task.Delay(160);
        BitmapSource img;
        try { img = CaptureService.CaptureMonitorUnderCursor(); }
        finally { if (!_fullscreenHidden) Root.Opacity = 1; }
        new ScreenshotEditor(img).Show();
    }

    private async Task OcrFromScreenAsync()
    {
        var img = await PickRegionAsync();
        if (img == null) return;
        var text = await CaptureService.RecognizeTextAsync(img);
        if (text.Length == 0)
        {
            ShowToast("", "Текст не найден", "Попробуйте выделить крупнее");
            return;
        }
        Clipboard.SetText(text);
        ShowToast("", "Текст скопирован", text.Replace(Environment.NewLine, " "), () =>
        {
            var editor = new ScreenshotEditor(img);
            editor.Show();
            _ = editor.RunOcrAsync();
        });
    }

    private async Task QrFromScreenAsync()
    {
        var img = await PickRegionAsync();
        if (img == null) return;
        var text = CaptureService.DecodeQr(img);
        if (text == null)
        {
            ShowToast("", "QR-код не найден", "Выделите код целиком");
            return;
        }
        Clipboard.SetText(text);
        bool link = Services.SafeShell.IsWebUrl(text);
        ShowToast("", link ? "Ссылка из QR — нажмите, чтобы открыть" : "QR-код скопирован", text,
            link ? () => Services.SafeShell.OpenUrl(text) : null, ms: 4500);
    }

    private async Task PickColorAsync()
    {
        var (desktop, _) = await CaptureWithoutIslandAsync();
        var c = await RegionPicker.PickColorAsync(desktop);
        if (c is not Color color) return;
        var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        Clipboard.SetText(hex);
        ShowToast("", $"{hex} скопирован", $"rgb({color.R}, {color.G}, {color.B})", null, new SolidColorBrush(color));
    }

    private void CleanClipboard()
    {
        if (!Clipboard.ContainsText())
        {
            ShowToast("", "В буфере нет текста", null);
            return;
        }
        var clean = CaptureService.CleanText(Clipboard.GetText());
        Clipboard.SetText(clean); // plain text only: formatting, links and styles are gone
        ShowToast("", "Текст очищен", "Без форматирования, лишних пробелов и невидимых символов");
    }

    private void RunCommand()
    {
        var cmd = _settings.Command.Trim();
        if (cmd.Length == 0)
        {
            OpenTab(TabSettings);
            Dispatcher.BeginInvoke(() =>
            {
                CommandBox.Focus();
                CommandBox.BringIntoView();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", "/c start \"\" " + cmd)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            });
            ShowToast("", "Команда запущена", cmd);
        }
        catch (Exception e) { ShowToast("", "Не удалось запустить", e.Message); }
    }

    // ------------------------------------------------------------------ recording

    private void InitRecording()
    {
        _recording.Finished += (path, error) => Dispatcher.BeginInvoke(() =>
        {
            BuildHome();
            UpdateEyesVisibility();
            if (_mode == Mode.Compact) MorphTo(TargetSize(Mode.Compact));
            if (path != null)
                ShowToast("", "Запись сохранена", System.IO.Path.GetFileName(path),
                    () => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }), ms: 5000);
            else
            {
                Log.Write("recording failed: " + error);
                ShowToast("", "Запись не удалась", error);
            }
        });
    }

    private void ToggleRecording()
    {
        if (_recording.IsRecording)
        {
            _recording.Stop();
            return;
        }
        _recording.Start(_settings.RecordSystemAudio, _settings.RecordMicrophone);
        BuildHome();
        if (_mode != Mode.Compact) SetMode(Mode.Compact);
        UpdateEyesVisibility();
        MorphTo(TargetSize(Mode.Compact));
        var what = (_settings.RecordSystemAudio, _settings.RecordMicrophone) switch
        {
            (true, true) => "со звуком и микрофоном",
            (true, false) => "со звуком компьютера",
            (false, true) => "с микрофоном",
            _ => "без звука",
        };
        ShowToast("", "Запись экрана началась", $"{what} · стоп: Ctrl+Alt+R или кнопка на «Главной»",
            null, (Brush)FindResource("Danger"), ms: 2200);
    }
}

using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// Command bar (Alt+Space): launch apps, calculate, start a timer, set a reminder, run island actions,
/// search the web or run a shell command - without leaving the keyboard.
/// </summary>
public partial class MainWindow
{
    private sealed record CommandItem(string Title, string Subtitle, string Glyph, Action Run, string? IconPath = null);

    private readonly AppIndex _apps = new();
    private List<CommandItem> _cmdItems = [];
    private int _cmdIndex;

    private void ToggleCommandBar()
    {
        if (_mode == Mode.Command) CloseCommandBar(); else OpenCommandBar();
    }

    private void OpenCommandBar()
    {
        if (_mode == Mode.Claude) return;
        if (_hidden) ShowIsland();
        _ = _apps.EnsureLoadedAsync();
        CmdBarBox.Text = "";
        RenderCommands();
        SetMode(Mode.Command);
        Activate();
        Native.SetForegroundWindow(_hwnd);
        Dispatcher.BeginInvoke(() =>
        {
            CmdBarBox.Focus();
            Keyboard.Focus(CmdBarBox);
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void CloseCommandBar()
    {
        if (_mode != Mode.Command) return;
        Keyboard.ClearFocus();
        SetMode(Mode.Compact);
    }

    private void CmdBarBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (CommandHint == null) return;
        CommandHint.Visibility = CmdBarBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        RenderCommands();
        Dispatcher.BeginInvoke(() => { if (_mode == Mode.Command) MorphTo(TargetSize(Mode.Command)); },
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void CmdBarBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                CloseCommandBar();
                e.Handled = true;
                break;
            case Key.Down:
                if (_cmdItems.Count > 0) { _cmdIndex = (_cmdIndex + 1) % _cmdItems.Count; HighlightCommand(); }
                e.Handled = true;
                break;
            case Key.Up:
                if (_cmdItems.Count > 0) { _cmdIndex = (_cmdIndex - 1 + _cmdItems.Count) % _cmdItems.Count; HighlightCommand(); }
                e.Handled = true;
                break;
            case Key.Enter:
                if (_cmdIndex < _cmdItems.Count) ExecuteCommandItem(_cmdItems[_cmdIndex]);
                e.Handled = true;
                break;
        }
    }

    private void ExecuteCommandItem(CommandItem item)
    {
        CloseCommandBar();
        try { item.Run(); }
        catch (Exception ex) { ShowToast("", L.T("Не получилось"), ex.Message); }
    }

    // ------------------------------------------------------------------ results

    private void RenderCommands()
    {
        _cmdItems = BuildCommands(CmdBarBox.Text);
        _cmdIndex = 0;
        CommandResults.Children.Clear();
        for (int i = 0; i < _cmdItems.Count; i++)
        {
            var item = _cmdItems[i];
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            FrameworkElement icon;
            var appIcon = item.IconPath != null ? _apps.Icon(item.IconPath) : null;
            if (appIcon != null)
                icon = new Image { Source = appIcon, Width = 26, Height = 26, Margin = new Thickness(2, 0, 2, 0) };
            else
            {
                var chip = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(10) };
                chip.SetResourceReference(Border.BackgroundProperty, "ChipBg");
                chip.Child = new TextBlock
                {
                    Text = item.Glyph, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                };
                icon = chip;
            }
            row.Children.Add(icon);

            var title = new TextBlock { Text = item.Title, FontSize = 13.5, Margin = new Thickness(12, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(title, 1);
            row.Children.Add(title);
            var sub = new TextBlock { Text = item.Subtitle, Style = (Style)FindResource("Muted"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(sub, 2);
            row.Children.Add(sub);

            var border = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(8, 6, 12, 6), Margin = new Thickness(0, 1, 0, 1), Cursor = Cursors.Hand, Background = Brushes.Transparent, Child = row };
            int index = i;
            border.MouseEnter += (_, _) => { _cmdIndex = index; HighlightCommand(); };
            border.MouseLeftButtonUp += (_, _) => ExecuteCommandItem(item);
            CommandResults.Children.Add(border);
        }
        HighlightCommand();
    }

    private void HighlightCommand()
    {
        for (int i = 0; i < CommandResults.Children.Count; i++)
        {
            var b = (Border)CommandResults.Children[i];
            if (i == _cmdIndex) b.SetResourceReference(Border.BackgroundProperty, "ChipBg");
            else b.Background = Brushes.Transparent;
        }
    }

    // island actions reachable by typing (Russian and English words)
    private (string[] Words, string Title, string Glyph, Action Run)[] IslandCommands =>
    [
        (["скриншот", "область", "screenshot"], L.T("Скриншот области"), "", () => RunAction("region")),
        (["весь экран", "экран", "fullscreen"], L.T("Скриншот всего экрана"), "", () => RunAction("full")),
        (["текст с экрана", "распознать", "ocr"], L.T("Текст с экрана"), "", () => RunAction("ocr")),
        (["qr", "куар", "код"], L.T("QR-код с экрана"), "", () => RunAction("qr")),
        (["пипетка", "цвет", "color", "picker"], L.T("Пипетка"), "", () => RunAction("picker")),
        (["запись", "record", "видео"], _recording.IsRecording ? L.T("Остановить запись экрана") : L.T("Запись экрана"), "", () => RunAction("record")),
        (["чистый текст", "очистить текст", "plain"], L.T("Чистый текст в буфере"), "", () => RunAction("cleantext")),
        (["тема", "theme", "светлая", "тёмная", "темная"], L.T("Сменить тему"), "", ToggleTheme),
        (["выровнять", "центр", "align", "center"], L.T("Выровнять по центру"), "", AlignIsland),
        (["обновить", "обновления", "update", "updates"], L.T("Проверить обновления"), "", () => _ = CheckForUpdatesAsync(manual: true)),
        (["спрятать", "скрыть", "hide"], L.T("Спрятать остров"), "", () => HideIsland(animate: true)),
        (["настройки", "settings"], L.T("Настройки"), "", () => OpenTab(TabSettings)),
        (["погода", "weather"], L.T("Погода"), "", () => OpenTab(TabWeather)),
        (["музыка", "music", "плеер"], L.T("Музыка"), "", () => OpenTab(TabMusic)),
        (["чат", "chat", "claude", "gemini", "deepseek", "ии", "ai"], L.T("Чат с ИИ"), "", () => OpenTab(TabChat)),
        (["буфер", "clipboard", "полка", "история"], L.T("Буфер обмена"), "", () => OpenTab(TabClip)),
        (["заметки", "заметка", "notes"], L.T("Заметки"), "", () => OpenTab(TabNotes)),
        (["календарь", "calendar", "напоминания"], L.T("Календарь"), "", () => OpenTab(TabCalendar)),
        (["система", "system", "cpu", "gpu"], L.T("Система"), "", () => OpenTab(TabSystem)),
        (["блокировка", "заблокировать", "lock"], L.T("Заблокировать компьютер"), "", () => Native.LockWorkStation()),
        (["не беспокоить", "фокус", "focus", "dnd"], FocusMode.IsOn ? L.T("Выключить «Не беспокоить»") : L.T("Не беспокоить на 1 час"), "",
            () => { if (FocusMode.IsOn) FocusMode.End(); else FocusMode.Start(TimeSpan.FromHours(1)); }),
        (["микрофон", "mute", "mic"], _audio.MicMuted == true ? L.T("Включить микрофон") : L.T("Выключить микрофон"), "",
            () => _audio.MicMuted = !(_audio.MicMuted ?? false)),
    ];

    private List<CommandItem> BuildCommands(string query)
    {
        var list = new List<CommandItem>();
        var q = query.Trim();
        var lower = q.ToLowerInvariant();

        if (q.Length == 0)
        {
            // a few useful things to start from
            foreach (var c in IslandCommands.Take(4))
                list.Add(new CommandItem(c.Title, L.T("действие"), c.Glyph, c.Run));
            list.Add(new CommandItem(L.T("Таймер на 5 минут"), L.T("таймер 5"), "", () => StartQuickTimer(TimeSpan.FromMinutes(5))));
            return list;
        }

        // > shell command
        if (q.StartsWith('>'))
        {
            var cmd = q[1..].Trim();
            if (cmd.Length > 0)
                list.Add(new CommandItem(L.F("Выполнить: {0}", cmd), "cmd", "",
                    () => Process.Start(new ProcessStartInfo("cmd.exe", "/k " + cmd) { UseShellExecute = true })));
            return list;
        }

        // calculator
        if (TryCalculate(q, out var result))
            list.Add(new CommandItem($"= {result}", L.T("Enter — скопировать"), "", () =>
            {
                Clipboard.SetText(result);
                ShowToast("", $"{q} = {result}", L.T("Скопировано"));
            }));

        // "таймер 10", "timer 90 сек", "таймер 2 ч"
        var tm = Regex.Match(lower, @"^(?:таймер|timer|temporizador|计时)\s+(\d+(?:[.,]\d+)?)\s*(с|сек|s|sec|ч|час|h|м|мин|min|m)?");
        if (tm.Success)
        {
            double n = double.Parse(tm.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            var unit = tm.Groups[2].Value;
            var span = unit.StartsWith('с') || unit.StartsWith('s') ? TimeSpan.FromSeconds(n)
                     : unit.StartsWith('ч') || unit == "h" ? TimeSpan.FromHours(n) : TimeSpan.FromMinutes(n);
            if (span.TotalSeconds is >= 1 and <= 36000)
                list.Add(new CommandItem(L.F("Таймер на {0}", FormatSpan(span)), L.T("запустить"), "", () => StartQuickTimer(span)));
        }

        // "напомни позвонить маме в 19:00", "remind … через 20 мин"
        var rm = Regex.Match(q, @"^(?:напомни(?:ть)?|нагадай|remind(?: me)?|recuérdame|erinnere(?: mich)?|lembre(?:-me)?|提醒)\s+(.+)$", RegexOptions.IgnoreCase);
        if (rm.Success)
        {
            var rest = rm.Groups[1].Value;
            var when = CalendarService.ParseWhen(rest);
            if (when != null)
            {
                var title = Regex.Replace(rest, @"(послезавтра|завтра|сегодня|через\s+\d+\s*\S+|\bв\s+\d{1,2}([:.]\d{2})?|\d{1,2}[:.]\d{2}|\d{1,2}\.\d{1,2}(\.\d{2,4})?)", "", RegexOptions.IgnoreCase).Trim(' ', ',', '.');
                if (title.Length == 0) title = L.T("Напоминание");
                list.Add(new CommandItem(L.F("Напомнить: {0}", title), when.Value.ToString(L.T("dd.MM в HH:mm")), "", () =>
                {
                    _settings.Reminders.Add(new Reminder { Title = title, When = when.Value });
                    _settings.Save();
                    ShowToast("", L.T("Напомню ") + when.Value.ToString(L.T("dd.MM в HH:mm")), title);
                }));
            }
        }

        // island actions
        foreach (var c in IslandCommands)
            if (c.Words.Any(w => w.StartsWith(lower) || (lower.Length >= 3 && w.Contains(lower))) ||
                c.Title.Contains(q, StringComparison.OrdinalIgnoreCase))
                list.Add(new CommandItem(c.Title, L.T("действие"), c.Glyph, c.Run));

        // installed apps
        foreach (var app in _apps.Search(q, 5))
            list.Add(new CommandItem(app.Name, L.T("приложение"), "", () => AppIndex.Launch(app.Path), app.Path));

        // link or web search
        if (Regex.IsMatch(q, @"^(https?://)?[\w-]+(\.[\w-]+)+(/\S*)?$") && !q.Contains(' '))
        {
            var url = q.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? q : "https://" + q;
            list.Add(new CommandItem(L.F("Открыть {0}", q), L.T("сайт"), "", () => Services.SafeShell.OpenUrl(url)));
        }
        list.Add(new CommandItem(L.F("Искать «{0}» в интернете", q), "Google", "",
            () => Services.SafeShell.OpenUrl("https://www.google.com/search?q=" + Uri.EscapeDataString(q))));

        return list.Take(8).ToList();
    }

    private void StartQuickTimer(TimeSpan span)
    {
        _timer.SetKind(TimerKind.Timer);
        _timer.SetDuration(span);
        _timer.Start();
        RenderTimer();
    }

    private static string FormatSpan(TimeSpan t) =>
        t.TotalHours >= 1 ? L.F("{0:0.#} ч", t.TotalHours) : t.TotalMinutes >= 1 ? L.F("{0:0.#} мин", t.TotalMinutes) : L.F("{0:0} с", t.TotalSeconds);

    private static bool TryCalculate(string q, out string result)
    {
        result = "";
        // needs a digit and an operator, and nothing but math characters
        if (!Regex.IsMatch(q, @"^[\d\s+\-*/().,%]+$") || !Regex.IsMatch(q, @"\d") || !Regex.IsMatch(q, @"[+\-*/%]")) return false;
        try
        {
            var value = new DataTable { Locale = CultureInfo.InvariantCulture }.Compute(q.Replace(',', '.'), null);
            if (value is null or DBNull) return false;
            var d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (double.IsNaN(d) || double.IsInfinity(d)) return false;
            result = Math.Round(d, 10).ToString("0.##########", CultureInfo.InvariantCulture);
            return true;
        }
        catch { return false; }
    }
}

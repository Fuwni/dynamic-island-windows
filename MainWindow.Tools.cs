using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>Timer, clipboard history, notes, calendar tabs and dropping files on the island.</summary>
public partial class MainWindow
{
    // ================================================================ timer tab

    private static readonly int[] TimerPresetMinutes = [1, 3, 5, 10, 15, 25, 45, 60];

    private void InitTimerPage()
    {
        foreach (var m in TimerPresetMinutes)
        {
            var b = new Button
            {
                Style = (Style)FindResource("Pill"),
                Content = m < 60 ? L.F("{0} мин", m) : L.T("1 ч"),
                Padding = new Thickness(11, 5, 11, 5),
                Margin = new Thickness(0, 0, 6, 6),
            };
            b.Click += (_, _) =>
            {
                _timer.SetKind(TimerKind.Timer);
                _timer.SetDuration(TimeSpan.FromMinutes(m));
                TimerMinutesBox.Text = m.ToString();
                _timer.Start();
                RenderTimer();
            };
            TimerPresets.Children.Add(b);
        }
        RenderTimer();
    }

    private void TimerMode_Checked(object sender, RoutedEventArgs e)
    {
        if (TimerPresets == null) return; // XAML still loading
        var kind = TimerModePomodoro.IsChecked == true ? TimerKind.Pomodoro
                 : TimerModeStopwatch.IsChecked == true ? TimerKind.Stopwatch : TimerKind.Timer;
        if (_timer.Kind != kind)
        {
            if (_focusByPomodoro) { FocusMode.End(); _focusByPomodoro = false; }
            _timer.SetKind(kind);
            if (kind == TimerKind.Timer && int.TryParse(TimerMinutesBox.Text, out var m) && m > 0)
                _timer.SetDuration(TimeSpan.FromMinutes(m));
        }
        RenderTimer();
    }

    private void RenderTimer()
    {
        if (TimerPresets == null) return;
        var kind = _timer.Kind;
        (kind == TimerKind.Pomodoro ? TimerModePomodoro : kind == TimerKind.Stopwatch ? TimerModeStopwatch : TimerModeTimer).IsChecked = true;
        TimerPresets.Visibility = kind == TimerKind.Timer ? Visibility.Visible : Visibility.Collapsed;
        TimerCustomRow.Visibility = kind == TimerKind.Timer ? Visibility.Visible : Visibility.Collapsed;
        PomodoroFocusSwitch.Visibility = kind == TimerKind.Pomodoro ? Visibility.Visible : Visibility.Collapsed;
        UpdateTimerPage();
        Remeasure();
    }

    private void UpdateTimerPage()
    {
        TimerBig.Text = _timer.Display;
        TimerPhase.Text = _timer.Phase;
        TimerRing.Data = _timer.Progress > 0 ? RingGeometry(132, 7, _timer.Progress) : null;
        TimerRing.Stroke = (Brush)FindResource(_timer.Kind == TimerKind.Pomodoro && !_timer.PomodoroWork ? "AccentBlue" : "Accent");
        TimerStartButton.Content = _timer.Running ? L.T("Пауза") : _timer.Active ? L.T("Продолжить") : L.T("Старт");
    }

    private void TimerStart_Click(object sender, RoutedEventArgs e)
    {
        if (_timer.Running) { _timer.Pause(); UpdateTimerPage(); return; }

        if (_timer.Kind == TimerKind.Timer && !_timer.Active)
        {
            if (!int.TryParse(TimerMinutesBox.Text.Trim(), out var m) || m <= 0 || m > 600) m = 5;
            _timer.SetDuration(TimeSpan.FromMinutes(m));
        }
        _timer.Start();
        if (_timer.Kind == TimerKind.Pomodoro && _timer.PomodoroWork && _settings.PomodoroFocus && !FocusMode.IsOn)
        {
            FocusMode.Start(_timer.Remaining);
            _focusByPomodoro = true;
        }
        UpdateTimerPage();
    }

    private void TimerReset_Click(object sender, RoutedEventArgs e)
    {
        _timer.Reset();
        if (_focusByPomodoro) { FocusMode.End(); _focusByPomodoro = false; }
        UpdateTimerPage();
    }

    private void TimerMinutesBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        TimerStart_Click(sender, e);
        Keyboard.ClearFocus();
    }

    private void PomodoroFocus_Click(object sender, RoutedEventArgs e)
    {
        _settings.PomodoroFocus = PomodoroFocusSwitch.IsChecked == true;
        _settings.Save();
    }

    // ================================================================ clipboard history

    private sealed record ClipItem(string? Text, BitmapSource? Image, string[]? Files, DateTime At);

    private readonly List<ClipItem> _clips = [];
    private IntPtr _lastExternalWindow;

    private bool OwnsWindow(IntPtr h)
    {
        Native.GetWindowThreadProcessId(h, out uint pid);
        return pid == (uint)Environment.ProcessId;
    }

    /// <summary>
    /// Password managers (KeePass, Bitwarden, 1Password, Windows itself…) mark copied secrets as
    /// "not for clipboard history / monitors". Such copies never enter the island's history.
    /// </summary>
    private static bool IsPrivateClip()
    {
        try
        {
            if (Clipboard.ContainsData("ExcludeClipboardContentFromMonitorProcessing")) return true;
            if (Clipboard.ContainsData("Clipboard Viewer Ignore")) return true;
            foreach (var f in new[] { "CanIncludeInClipboardHistory", "CanUploadToCloudClipboard" })
            {
                if (!Clipboard.ContainsData(f)) continue;
                if (Clipboard.GetData(f) is System.IO.MemoryStream ms && ms.Length >= 4)
                {
                    var b = ms.ToArray();
                    if (BitConverter.ToInt32(b, 0) == 0) return true;
                }
            }
        }
        catch { }
        return false;
    }

    private async void OnClipboardChanged()
    {
        if (IsPrivateClip()) return;
        ClipItem? item = null;
        for (int attempt = 0; attempt < 3 && item == null; attempt++)
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    var t = Clipboard.GetText();
                    if (!string.IsNullOrWhiteSpace(t) && t.Length < 200_000) item = new ClipItem(t, null, null, DateTime.Now);
                }
                else if (Clipboard.ContainsImage())
                {
                    var img = Clipboard.GetImage();
                    if (img != null) { img.Freeze(); item = new ClipItem(null, img, null, DateTime.Now); }
                }
                else if (Clipboard.ContainsFileDropList())
                {
                    var files = Clipboard.GetFileDropList().Cast<string>().ToArray();
                    if (files.Length > 0) item = new ClipItem(null, null, files, DateTime.Now);
                }
                break;
            }
            catch { await Task.Delay(60); } // clipboard busy
        }
        if (item == null) return;

        if (item.Text != null) _clips.RemoveAll(c => c.Text == item.Text); // copying again moves it up
        _clips.Insert(0, item);
        if (_clips.Count > 30) _clips.RemoveRange(30, _clips.Count - 30);
        if (_mode == Mode.Expanded && TabClip.IsChecked == true && ClipSegHistory.IsChecked == true) RenderClipboard();
    }

    private void RenderClipboard()
    {
        ClipList.Children.Clear();
        if (_clips.Count == 0)
        {
            ClipList.Children.Add(new TextBlock { Text = L.T("Скопируйте что-нибудь (Ctrl+C) — оно появится здесь"), Style = (Style)FindResource("Muted"), Margin = new Thickness(4, 6, 0, 6) });
            Remeasure();
            return;
        }
        foreach (var c in _clips)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            FrameworkElement content = c.Image != null
                ? new Image { Source = c.Image, MaxHeight = 70, HorizontalAlignment = HorizontalAlignment.Left, Stretch = Stretch.Uniform }
                : new TextBlock
                {
                    Text = c.Text ?? string.Join(", ", c.Files!.Select(Path.GetFileName)),
                    TextWrapping = TextWrapping.Wrap, MaxHeight = 38, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12.5,
                };
            row.Children.Add(content);
            var time = new TextBlock
            {
                Text = (c.Files != null ? L.T("файлы · ") : "") + c.At.ToString("HH:mm"),
                Style = (Style)FindResource("Muted"), FontSize = 10.5, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top,
            };
            Grid.SetColumn(time, 1);
            row.Children.Add(time);

            var tile = new Border { Style = (Style)FindResource("Tile"), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 6), Cursor = Cursors.Hand, Child = row };
            tile.MouseEnter += (_, _) => tile.SetResourceReference(Border.BackgroundProperty, "TileBgHover");
            tile.MouseLeave += (_, _) => tile.SetResourceReference(Border.BackgroundProperty, "TileBg");
            var item = c;
            tile.MouseLeftButtonUp += async (_, _) => await PasteClipAsync(item);
            ClipList.Children.Add(tile);
        }
        Remeasure();
    }

    private async Task PasteClipAsync(ClipItem c)
    {
        try
        {
            if (c.Text != null) Clipboard.SetText(c.Text);
            else if (c.Image != null) Clipboard.SetImage(c.Image);
            else if (c.Files != null) FileDropActions.CopyFiles(c.Files);
        }
        catch { ShowToast("", L.T("Буфер обмена занят"), null); return; }

        SetMode(Mode.Compact);
        var target = _lastExternalWindow;
        if (target != IntPtr.Zero && Native.IsWindow(target))
        {
            Native.SetForegroundWindow(target);
            await Task.Delay(160);
            // Ctrl+V into the window the user was typing in
            Native.keybd_event(0x11, 0, 0, UIntPtr.Zero);
            Native.keybd_event(0x56, 0, 0, UIntPtr.Zero);
            Native.keybd_event(0x56, 0, 2, UIntPtr.Zero);
            Native.keybd_event(0x11, 0, 2, UIntPtr.Zero);
        }
        else ShowToast("", L.T("Скопировано"), L.T("Вставьте Ctrl+V"));
    }

    private void ClipClear_Click(object sender, RoutedEventArgs e)
    {
        if (ClipSegShelf.IsChecked == true) { _settings.Shelf.Clear(); _settings.Save(); }
        else if (ClipSegText.IsChecked == true) { TextToolsBox.Clear(); QrFrame.Visibility = Visibility.Collapsed; }
        else _clips.Clear();
        RenderClipSection();
    }

    // ================================================================ notes

    private void NewNoteBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            int i = NewNoteBox.CaretIndex;
            NewNoteBox.Text = NewNoteBox.Text.Insert(i, Environment.NewLine);
            NewNoteBox.CaretIndex = i + Environment.NewLine.Length;
            e.Handled = true;
            return;
        }
        var text = NewNoteBox.Text.Trim();
        e.Handled = true;
        if (text.Length == 0) return;
        NotesStore.Notes.Insert(0, new Note { Text = text });
        NotesStore.Save();
        NewNoteBox.Clear();
        RenderNotes();
    }

    private void RenderNotes()
    {
        NotesList.Children.Clear();
        foreach (var n in NotesStore.Notes)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = n.Text, TextWrapping = TextWrapping.Wrap, FontSize = 13, MaxHeight = 80, TextTrimming = TextTrimming.CharacterEllipsis });
            body.Children.Add(new TextBlock { Text = n.Created.ToString("d MMMM, HH:mm"), Style = (Style)FindResource("Muted"), FontSize = 10.5, Margin = new Thickness(0, 4, 0, 0) });
            row.Children.Add(body);

            var del = new Button { Style = (Style)FindResource("IconButton"), Content = "", Width = 28, Height = 28, FontSize = 12, ToolTip = L.T("Удалить"), VerticalAlignment = VerticalAlignment.Top };
            var note = n;
            del.Click += (_, _) => { NotesStore.Notes.Remove(note); NotesStore.Save(); RenderNotes(); };
            Grid.SetColumn(del, 1);
            row.Children.Add(del);

            var tile = new Border { Style = (Style)FindResource("Tile"), Padding = new Thickness(12, 9, 6, 9), Margin = new Thickness(0, 0, 0, 6), Cursor = Cursors.Hand, Child = row };
            body.MouseLeftButtonUp += (_, _) =>
            {
                try { Clipboard.SetText(note.Text); } catch { }
                ShowToast("", L.T("Заметка скопирована"), null);
            };
            NotesList.Children.Add(tile);
        }
        if (NotesStore.Notes.Count == 0)
            NotesList.Children.Add(new TextBlock { Text = L.T("Пока пусто — напишите мысль выше"), Style = (Style)FindResource("Muted"), Margin = new Thickness(4, 4, 0, 4) });
        Remeasure();
    }

    // ================================================================ calendar

    private void RenderCalendar()
    {
        EventsList.Children.Clear();
        var events = _calendar.Upcoming(7);
        foreach (var e in events.Take(8))
        {
            var when = e.Start.Date == DateTime.Today ? L.T("Сегодня") : e.Start.Date == DateTime.Today.AddDays(1) ? L.T("Завтра") : e.Start.ToString("ddd d MMM");
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = e.AllDay ? when : $"{when} {e.Start:HH:mm}", Style = (Style)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center });
            var title = new TextBlock { Text = (e.IsReminder ? "🔔 " : "") + e.Title, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(title, 1);
            row.Children.Add(title);
            if (e.IsReminder)
            {
                var del = new Button { Style = (Style)FindResource("IconButton"), Content = "", Width = 26, Height = 26, FontSize = 11 };
                var ev = e;
                del.Click += (_, _) =>
                {
                    _settings.Reminders.RemoveAll(r => r.Title == ev.Title && r.When == ev.Start);
                    _settings.Save();
                    RenderCalendar();
                };
                Grid.SetColumn(del, 2);
                row.Children.Add(del);
            }
            EventsList.Children.Add(row);
        }
        if (events.Count == 0)
            EventsList.Children.Add(new TextBlock { Text = L.T("На неделю ничего не запланировано"), Style = (Style)FindResource("Muted") });
        IcsStatus.Text = _calendar.Error != null
            ? L.T("Не удалось загрузить календарь: ") + _calendar.Error
            : _settings.CalendarIcs.Length > 0 ? L.T("Календарь подключён, обновляется каждые 15 минут.")
            : L.T("Google Календарь → Настройки календаря → «Секретный адрес в формате iCal».");
        Remeasure();
    }

    private void ReminderAdd_Click(object sender, RoutedEventArgs e)
    {
        var title = ReminderTitleBox.Text.Trim();
        var when = CalendarService.ParseWhen(ReminderWhenBox.Text);
        if (title.Length == 0 || when == null)
        {
            ReminderHint.Text = title.Length == 0 ? L.T("Напишите, о чём напомнить") : L.T("Не понял время. Примеры: 18:30, завтра 9:00, через 20 мин, 15.11 14:00");
            ReminderHint.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
            return;
        }
        _settings.Reminders.Add(new Reminder { Title = title, When = when.Value });
        _settings.Save();
        ReminderTitleBox.Clear();
        ReminderWhenBox.Clear();
        ReminderHint.Text = L.T("Напомню ") + when.Value.ToString(L.T("dd.MM в HH:mm"));
        ReminderHint.SetResourceReference(TextBlock.ForegroundProperty, "Fg2");
        Keyboard.ClearFocus();
        RenderCalendar();
    }

    private void Reminder_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) ReminderAdd_Click(sender, e);
    }

    private async void IcsApply_Click(object sender, RoutedEventArgs e)
    {
        _settings.CalendarIcs = IcsBox.Text.Trim();
        _settings.Save();
        IcsStatus.Text = L.T("Загружаю…");
        await _calendar.RefreshAsync(force: true);
        RenderCalendar();
    }

    // ================================================================ drop files on the island

    private string[] _dropped = [];

    private void Island_DragEnter(object sender, DragEventArgs e)
    {
        if (_draggingOut || !e.Data.GetDataPresent(DataFormats.FileDrop) || _mode == Mode.Claude) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        if (_mode == Mode.Expanded && HomeApps.IsVisible) { e.Effects = DragDropEffects.Link; return; }   // pin to the apps row
        if (_mode == Mode.Drop && DropActions.Visibility == Visibility.Visible) return;
        _dropped = [];
        DropIcon.Text = "";
        DropTitle.Text = L.T("Отпустите файл на острове");
        DropSubtitle.Text = L.T("Поделиться, ZIP, конвертировать или сжать картинку");
        DropActions.Visibility = Visibility.Collapsed;
        if (_hidden) ShowIsland();
        SetMode(Mode.Drop);
        Kick(2);
    }

    private void Island_DragLeave(object sender, DragEventArgs e)
    {
        var p = e.GetPosition(Island);
        bool outside = p.X < 0 || p.Y < 0 || p.X > Island.ActualWidth || p.Y > Island.ActualHeight;
        if (outside && _dropped.Length == 0 && _mode == Mode.Drop) SetMode(Mode.Compact);
    }

    private void Island_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        e.Handled = true;
        if (_mode == Mode.Expanded && HomeApps.IsVisible)
        {
            if (OverApps(e)) { AddApps(files); return; }
            SetMode(Mode.Drop);   // dropped elsewhere on Home: the usual file actions
        }
        _dropped = files;

        long size = files.Where(File.Exists).Sum(f => new FileInfo(f).Length);
        bool images = files.All(f => File.Exists(f) && FileDropActions.IsImage(f));
        DropIcon.Text = images ? "" : files.Any(Directory.Exists) ? "" : "";
        DropTitle.Text = files.Length == 1 ? Path.GetFileName(files[0].TrimEnd('\\')) : L.F("{0} файла(ов)", files.Length);
        DropSubtitle.Text = size > 0 ? DownloadWatcher.FormatBytes(size) : L.T("папка");

        DropActions.Children.Clear();
        AddDropAction("\uE7B8", L.T("На полку"), () => { AddToShelf(files); Done(L.T("На полке"), L.F("{0} файл(ов) ждут во вкладке «Буфер → Полка»", _settings.Shelf.Count)); });
        AddDropAction("", L.T("Поделиться"), () => FileDropActions.Share(_hwnd, files), keepOpen: true);
        AddDropAction("", L.T("Копировать для вставки"), () => { FileDropActions.CopyFiles(files); Done(L.T("Файлы в буфере"), L.T("Вставьте в Telegram, чат или папку: Ctrl+V")); });
        AddDropAction("", L.T("В ZIP"), () => Done(L.T("Архив готов"), null, FileDropActions.Zip(files)));
        if (images)
        {
            AddDropAction("", L.T("В JPG"), () => Done(L.T("Готово"), null, files.Select(f => FileDropActions.ConvertImage(f, "jpg")).Last()));
            AddDropAction("", L.T("В PNG"), () => Done(L.T("Готово"), null, files.Select(f => FileDropActions.ConvertImage(f, "png")).Last()));
            AddDropAction("", L.T("Сжать фото"), () => Done(L.T("Фото сжато"), null, files.Select(f => FileDropActions.ConvertImage(f, "jpg", 72, 1920)).Last()));
        }
        AddDropAction("", L.T("Копировать путь"), () => { Clipboard.SetText(string.Join(Environment.NewLine, files)); Done(L.T("Путь скопирован"), null); });
        AddDropAction("", L.T("Закрыть"), () => { _dropped = []; SetMode(Mode.Compact); }, keepOpen: true);

        DropActions.Visibility = Visibility.Visible;
        MorphTo(TargetSize(Mode.Drop));
        Kick(3);
    }

    private void AddDropAction(string glyph, string label, Action run, bool keepOpen = false)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("IconFont"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) });
        content.Children.Add(new TextBlock { Text = label });
        var b = new Button { Style = (Style)FindResource("Pill"), Content = content, Margin = new Thickness(0, 0, 6, 6) };
        b.Click += async (_, _) =>
        {
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                await Dispatcher.InvokeAsync(run, System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                Log.Write("drop action: " + ex);
                ShowToast("", L.T("Не получилось"), ex.Message);
            }
            finally { Mouse.OverrideCursor = null; }
        };
        DropActions.Children.Add(b);
    }

    private void Done(string title, string? text, string? file = null)
    {
        _dropped = [];
        Action? open = file == null ? null
            : () => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
        ShowToast("", title, text ?? (file != null ? Path.GetFileName(file) : null), open, (Brush)FindResource("Accent"), ms: 4000);
    }
}

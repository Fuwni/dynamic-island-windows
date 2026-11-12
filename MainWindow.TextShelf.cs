using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>Clipboard tab sections: file shelf and text tools.</summary>
public partial class MainWindow
{
    private bool _draggingOut;   // we are dragging a shelf file out: don't treat it as a drop onto the island
    private bool _textToolsBuilt;

    private void ClipSeg_Checked(object sender, RoutedEventArgs e)
    {
        if (TextPanel == null) return; // XAML still loading
        RenderClipSection();
    }

    private void RenderClipSection()
    {
        bool shelf = ClipSegShelf.IsChecked == true, text = ClipSegText.IsChecked == true;
        ClipHistoryPanel.Visibility = !shelf && !text ? Visibility.Visible : Visibility.Collapsed;
        ShelfPanel.Visibility = shelf ? Visibility.Visible : Visibility.Collapsed;
        TextPanel.Visibility = text ? Visibility.Visible : Visibility.Collapsed;

        if (shelf) RenderShelf();
        else if (text) BuildTextTools();
        else RenderClipboard();
        Remeasure();
    }

    // ================================================================ shelf

    private void AddToShelf(IEnumerable<string> files)
    {
        foreach (var f in files)
            if (!_settings.Shelf.Contains(f, StringComparer.OrdinalIgnoreCase))
                _settings.Shelf.Add(f);
        _settings.Save();
    }

    private void RenderShelf()
    {
        ShelfList.Children.Clear();
        // files that were moved or deleted meanwhile just drop off the shelf
        int before = _settings.Shelf.Count;
        _settings.Shelf.RemoveAll(p => !File.Exists(p) && !Directory.Exists(p));
        if (_settings.Shelf.Count != before) _settings.Save();

        if (_settings.Shelf.Count == 0)
        {
            ShelfList.Children.Add(new TextBlock { Text = L.T("Полка пуста"), Style = (Style)FindResource("Muted"), Margin = new Thickness(4, 4, 0, 4) });
            return;
        }

        if (_settings.Shelf.Count > 1)
        {
            var all = new Border { Style = (Style)FindResource("Tile"), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 6), Cursor = Cursors.SizeAll };
            all.Child = new TextBlock { Text = L.F("⠿  Потяните отсюда, чтобы забрать все {0}", _settings.Shelf.Count), FontSize = 12.5 };
            MakeDraggable(all, () => _settings.Shelf.ToArray());
            ShelfList.Children.Add(all);
        }

        foreach (var path in _settings.Shelf.ToList())
        {
            bool dir = Directory.Exists(path);
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var chip = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(10) };
            chip.SetResourceReference(Border.BackgroundProperty, "ChipBg");
            chip.Child = new TextBlock
            {
                Text = dir ? "" : FileDropActions.IsImage(path) ? "" : "",
                FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            row.Children.Add(chip);

            var text = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = Path.GetFileName(path.TrimEnd('\\')), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock
            {
                Text = (dir ? L.T("папка") : DownloadWatcher.FormatBytes(new FileInfo(path).Length)) + " · " + Path.GetDirectoryName(path),
                Style = (Style)FindResource("Muted"), FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis,
            });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var open = new Button { Style = (Style)FindResource("IconButton"), Content = "", Width = 28, Height = 28, FontSize = 12, ToolTip = L.T("Показать в папке") };
            open.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            var del = new Button { Style = (Style)FindResource("IconButton"), Content = "", Width = 28, Height = 28, FontSize = 11, ToolTip = L.T("Убрать с полки (файл останется)") };
            del.Click += (_, _) => { _settings.Shelf.Remove(path); _settings.Save(); RenderShelf(); Remeasure(); };
            buttons.Children.Add(open);
            buttons.Children.Add(del);
            Grid.SetColumn(buttons, 2);
            row.Children.Add(buttons);

            var tile = new Border { Style = (Style)FindResource("Tile"), Padding = new Thickness(10, 8, 6, 8), Margin = new Thickness(0, 0, 0, 6), Cursor = Cursors.SizeAll, Child = row };
            MakeDraggable(tile, () => [path]);
            ShelfList.Children.Add(tile);
        }
    }

    /// <summary>Press and pull: the files go wherever you drop them (Explorer, a chat, an editor…).</summary>
    private void MakeDraggable(FrameworkElement el, Func<string[]> files)
    {
        Point? down = null;
        el.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && IsButton(d)) return;
            down = e.GetPosition(el);
        };
        el.PreviewMouseLeftButtonUp += (_, _) => down = null;
        el.MouseMove += (_, e) =>
        {
            if (down is not Point p || e.LeftButton != MouseButtonState.Pressed) return;
            var now = e.GetPosition(el);
            if (Math.Abs(now.X - p.X) < 5 && Math.Abs(now.Y - p.Y) < 5) return;
            down = null;
            _draggingOut = true;
            try { DragDrop.DoDragDrop(el, new DataObject(DataFormats.FileDrop, files()), DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link); }
            finally { _draggingOut = false; }
            RenderShelf(); // a moved file disappears from the shelf
            Remeasure();
        };
    }

    private static bool IsButton(DependencyObject d)
    {
        for (var x = d; x != null; x = x is Visual ? VisualTreeHelper.GetParent(x) : LogicalTreeHelper.GetParent(x))
            if (x is Button) return true;
        return false;
    }

    // ================================================================ text tools

    private void BuildTextTools()
    {
        if (!_textToolsBuilt)
        {
            _textToolsBuilt = true;
            void Add(string label, Func<string, string> op, string? tip = null)
            {
                var b = new Button { Style = (Style)FindResource("ToolChip"), Content = label, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 6, 6), ToolTip = tip };
                b.Click += (_, _) => ApplyTextTool(op);
                TextToolButtons.Children.Add(b);
            }
            Add(L.T("Раскладка"), TextTools.SwapLayout, L.T("ghbdtn → привет: текст, набранный не в той раскладке"));
            Add(L.T("ВЕРХНИЙ"), TextTools.Upper);
            Add(L.T("нижний"), TextTools.Lower);
            Add(L.T("Как в предложении"), TextTools.Sentence);
            Add(L.T("Транслит"), TextTools.Transliterate, L.T("Привет → Privet"));
            Add(L.T("Чистый текст"), CaptureService.CleanText, L.T("Убрать лишние пробелы, невидимые символы и «умные» кавычки"));
            Add("JSON", TextTools.JsonPretty, L.T("Красиво отформатировать JSON"));
            Add("→ Base64", TextTools.Base64Encode);
            Add("Base64 →", TextTools.Base64Decode);
            Add("→ URL", TextTools.UrlEncode);
            Add("URL →", TextTools.UrlDecode);

            var qr = new Button { Style = (Style)FindResource("ToolChip"), Content = L.T("QR-код"), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 6, 6), ToolTip = L.T("Сделать QR-код из текста или ссылки") };
            qr.Click += (_, _) => MakeQr();
            TextToolButtons.Children.Add(qr);

            var tr = new Button { Style = (Style)FindResource("TintPill"), Background = TintBrush("#0A84FF"), Foreground = TintText("#5AB0FF"), Content = L.T("Перевести"), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 6, 6) };
            tr.Click += async (_, _) => await TranslateTextAsync();
            TextToolButtons.Children.Add(tr);

            var paste = new Button { Style = (Style)FindResource("GhostPill"), Content = L.T("Вставить из буфера"), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 6, 6) };
            paste.Click += (_, _) => { try { if (Clipboard.ContainsText()) TextToolsBox.Text = Clipboard.GetText(); } catch { } };
            TextToolButtons.Children.Add(paste);

            var copy = new Button { Style = (Style)FindResource("AccentPill"), Content = L.T("Копировать"), Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 6, 6) };
            copy.Click += (_, _) =>
            {
                try { Clipboard.SetText(TextToolsBox.Text); TextToolsStatus.Text = L.T("Скопировано"); } catch { }
            };
            TextToolButtons.Children.Add(copy);
        }

        // start from what is on the clipboard: usually that's the text you want to fix
        if (TextToolsBox.Text.Length == 0)
        {
            try { if (Clipboard.ContainsText()) TextToolsBox.Text = Clipboard.GetText(); } catch { }
        }
        TextToolsStatus.Text = TextTools.Stats(TextToolsBox.Text);
    }

    private void TextToolsBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TextToolsStatus != null) TextToolsStatus.Text = TextTools.Stats(TextToolsBox.Text);
    }

    private void ApplyTextTool(Func<string, string> op)
    {
        // work on the selection if there is one, otherwise on everything
        bool selection = TextToolsBox.SelectionLength > 0;
        var input = selection ? TextToolsBox.SelectedText : TextToolsBox.Text;
        if (input.Length == 0) { TextToolsStatus.Text = L.T("Сначала введите или вставьте текст"); return; }
        try
        {
            var output = op(input);
            if (selection) TextToolsBox.SelectedText = output; else TextToolsBox.Text = output;
            QrFrame.Visibility = Visibility.Collapsed;
            Remeasure();
        }
        catch (Exception ex)
        {
            TextToolsStatus.Text = L.T("Не получилось: ") + ex.Message;
        }
    }

    private void MakeQr()
    {
        var text = TextToolsBox.Text.Trim();
        if (text.Length == 0) { TextToolsStatus.Text = L.T("Сначала введите текст или ссылку"); return; }
        try
        {
            var qr = TextTools.QrCode(text);
            QrImage.Source = qr;
            QrFrame.Visibility = Visibility.Visible;
            Clipboard.SetImage(qr);
            TextToolsStatus.Text = L.T("QR-код готов и скопирован как картинка");
        }
        catch (Exception ex) { TextToolsStatus.Text = L.T("Не получилось: ") + ex.Message; }
        Remeasure();
    }

    private async Task TranslateTextAsync()
    {
        var text = TextToolsBox.Text;
        if (text.Trim().Length == 0) return;
        TextToolsStatus.Text = L.T("Перевожу…");
        try
        {
            var (result, from, to) = await CaptureService.TranslateDetailedAsync(text, _settings.TranslateTarget);
            TextToolsBox.Text = result;
            TextToolsStatus.Text = $"{from.ToUpperInvariant()} → {to.ToUpperInvariant()}";
        }
        catch (Exception ex)
        {
            Log.Write("translate: " + ex.Message);
            TextToolsStatus.Text = L.T("Нет связи с сервисом перевода");
        }
    }
}

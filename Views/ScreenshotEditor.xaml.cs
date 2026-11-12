using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

public partial class ScreenshotEditor : Window
{
    private readonly BitmapSource _image;
    private readonly Stack<Stroke> _undo = new();
    private Color _color = Color.FromRgb(0xFF, 0x45, 0x3A);
    private bool _marker;
    private string? _ocrText;
    private string? _link;

    public ScreenshotEditor(BitmapSource image)
    {
        InitializeComponent();
        Loc.TranslateTree(this);
        _image = image;

        Picture.Source = image;
        Surface.Width = Picture.Width = Ink.Width = image.PixelWidth;
        Surface.Height = Picture.Height = Ink.Height = image.PixelHeight;

        // fit the picture into ~80% of the screen
        var work = SystemParameters.WorkArea;
        double dpi = VisualTreeHelper.GetDpi(Application.Current.MainWindow!).DpiScaleX;
        double w = image.PixelWidth / dpi, h = image.PixelHeight / dpi;
        double k = Math.Min(1, Math.Min(work.Width * 0.8 / w, (work.Height * 0.8 - 120) / h));
        // the toolbar needs ~640 px; small crops are centred inside
        CanvasHost.Width = Math.Max(640, w * k);
        CanvasHost.Height = Math.Max(160, h * k);
        Zoom.MaxWidth = w * Math.Max(k, 1);
        Zoom.MaxHeight = h * Math.Max(k, 1);

        Ink.StrokeCollected += (_, e) => _undo.Push(e.Stroke);
        ApplyPen();

        KeyDown += OnKeyDown;
        Loaded += (_, _) => Activate();
    }

    private double PixelsPerScreenUnit => _image.PixelWidth / Math.Max(1, CanvasHost.Width);

    private void ApplyPen()
    {
        Ink.EditingMode = InkCanvasEditingMode.Ink;
        double size = (_marker ? 18 : 4) * PixelsPerScreenUnit;
        Ink.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = _marker ? Color.FromArgb(0x70, _color.R, _color.G, _color.B) : _color,
            Width = size,
            Height = size,
            FitToCurve = true,
            IsHighlighter = _marker,
            StylusTip = _marker ? StylusTip.Rectangle : StylusTip.Ellipse,
        };
        PenButton.Opacity = _marker ? 0.5 : 1;
        MarkerButton.Opacity = _marker ? 1 : 0.5;
        EraserButton.Opacity = 0.5;
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        _color = (Color)ColorConverter.ConvertFromString((string)((Button)sender).Tag);
        ApplyPen();
    }

    private void Pen_Click(object sender, RoutedEventArgs e) { _marker = false; ApplyPen(); }
    private void Marker_Click(object sender, RoutedEventArgs e) { _marker = true; ApplyPen(); }

    private void Eraser_Click(object sender, RoutedEventArgs e)
    {
        Ink.EditingMode = InkCanvasEditingMode.EraseByStroke;
        EraserButton.Opacity = 1;
        PenButton.Opacity = MarkerButton.Opacity = 0.5;
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        while (_undo.Count > 0)
        {
            var s = _undo.Pop();
            if (Ink.Strokes.Contains(s)) { Ink.Strokes.Remove(s); break; }
        }
    }

    /// <summary>Picture + drawings at full resolution.</summary>
    private BitmapSource Render()
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawImage(_image, new Rect(0, 0, _image.PixelWidth, _image.PixelHeight));
            Ink.Strokes.Draw(dc);
        }
        var rtb = new RenderTargetBitmap(_image.PixelWidth, _image.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetImage(Render()); Flash(L.T("Скопировано")); }
        catch { Flash(L.T("Буфер обмена занят")); }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var path = CaptureService.SavePng(Render());
        Flash(L.T("Сохранено"));
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); } catch { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void Ocr_Click(object sender, RoutedEventArgs e) => await RunOcrAsync();

    public async Task RunOcrAsync()
    {
        ShowResult(L.T("ТЕКСТ"), L.T("Распознаю…"));
        try
        {
            _ocrText = await CaptureService.RecognizeTextAsync(_image);
            ShowResult(L.T("ТЕКСТ"), _ocrText.Length > 0 ? _ocrText : L.T("Текст не найден"));
        }
        catch (Exception ex) { ShowResult(L.T("ТЕКСТ"), ex.Message); }
    }

    private async void Translate_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_ocrText)) await RunOcrAsync();
        if (string.IsNullOrEmpty(_ocrText)) return;
        var to = Settings.Current.TranslateTarget;
        ShowResult(L.T("ПЕРЕВОД"), L.T("Перевожу…"));
        try
        {
            var (text, from, target) = await CaptureService.TranslateDetailedAsync(_ocrText, to);
            ShowResult(L.F("ПЕРЕВОД {0} → {1}", from.ToUpperInvariant(), target.ToUpperInvariant()), text);
        }
        catch { ShowResult(L.T("ПЕРЕВОД"), L.T("Нет связи с сервисом перевода")); }
    }

    private void Qr_Click(object sender, RoutedEventArgs e)
    {
        var text = CaptureService.DecodeQr(_image);
        _link = Services.SafeShell.IsWebUrl(text) ? text : null;
        ShowResult(L.T("QR-КОД"), text ?? L.T("QR-код не найден"));
    }

    private void ShowResult(string title, string text)
    {
        ResultPanel.Visibility = Visibility.Visible;
        ResultTitle.Text = title;
        ResultText.Text = text;
        OpenLinkButton.Visibility = _link != null && title.StartsWith("QR") ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CopyText_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(ResultText.Text); Flash(L.T("Скопировано")); } catch { }
    }

    private void OpenLink_Click(object sender, RoutedEventArgs e)
    {
        Services.SafeShell.OpenUrl(_link);
    }

    private void Flash(string text) => (Application.Current.MainWindow as MainWindow)?.ShowToast("", text, null);

    private void Toolbar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement fe && fe.TemplatedParent is Button) return;
        DragMove();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
        else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) Undo_Click(this, e);
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control && !ResultText.IsKeyboardFocusWithin) Copy_Click(this, e);
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) Save_Click(this, e);
    }
}

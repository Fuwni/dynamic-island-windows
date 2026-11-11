using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

/// <summary>
/// Full-screen overlay over a frozen screenshot: drag to select a region,
/// or (colour mode) click a pixel with a magnifier.
/// </summary>
public sealed class RegionPicker : Window
{
    private enum Mode { Region, Color }

    private readonly BitmapSource _desktop;
    private readonly Mode _mode;
    private readonly double _scale; // physical px per DIP
    private readonly Canvas _canvas = new();
    private readonly Path _dim = new() { Fill = new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)), IsHitTestVisible = false };
    private readonly Rectangle _frame = new()
    {
        Stroke = Brushes.White, StrokeThickness = 2, RadiusX = 6, RadiusY = 6,
        IsHitTestVisible = false, Visibility = Visibility.Collapsed,
    };
    private readonly Border _label = new()
    {
        Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x1C, 0x1C, 0x1E)),
        CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 4, 9, 4),
        IsHitTestVisible = false, Visibility = Visibility.Collapsed,
    };
    private readonly TextBlock _labelText = new() { Foreground = Brushes.White, FontSize = 12 };

    // colour mode
    private readonly Border _loupe = new()
    {
        Width = 128, Height = 128, CornerRadius = new CornerRadius(64),
        BorderBrush = Brushes.White, BorderThickness = new Thickness(3), IsHitTestVisible = false,
    };
    private readonly ImageBrush _loupeBrush;

    private Point? _start;
    private readonly TaskCompletionSource<object?> _result = new();

    private RegionPicker(BitmapSource desktop, Mode mode)
    {
        _desktop = desktop;
        _mode = mode;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Black;
        Cursor = Cursors.Cross;

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        _scale = desktop.PixelWidth / Width;

        var image = new Image { Source = desktop, Width = Width, Height = Height, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        _canvas.Children.Add(image);
        _canvas.Children.Add(_dim);
        _canvas.Children.Add(_frame);
        _label.Child = _labelText;
        _canvas.Children.Add(_label);

        _loupeBrush = new ImageBrush(desktop)
        {
            ViewboxUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill,
        };
        RenderOptions.SetBitmapScalingMode(_loupeBrush, BitmapScalingMode.NearestNeighbor);

        if (mode == Mode.Color)
        {
            var grid = new Grid();
            grid.Children.Add(new Border { CornerRadius = new CornerRadius(62), Background = _loupeBrush });
            grid.Children.Add(new Rectangle { Width = 9, Height = 9, Stroke = Brushes.White, StrokeThickness = 1.5 });
            _loupe.Child = grid;
            _loupe.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, Opacity = 0.5, ShadowDepth = 2 };
            _canvas.Children.Add(_loupe);
            _dim.Fill = Brushes.Transparent;
        }
        UpdateDim(Rect.Empty);

        Content = _canvas;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Finish(null); };
        MouseRightButtonUp += (_, _) => Finish(null);
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        Loaded += (_, _) => { Activate(); Focus(); };
        Closed += (_, _) => _result.TrySetResult(null);
    }

    public static async Task<(BitmapSource Image, Int32Rect Rect)?> PickRegionAsync(BitmapSource desktop)
    {
        var w = new RegionPicker(desktop, Mode.Region);
        w.Show();
        return await w._result.Task as (BitmapSource, Int32Rect)?;
    }

    public static async Task<Color?> PickColorAsync(BitmapSource desktop)
    {
        var w = new RegionPicker(desktop, Mode.Color);
        w.Show();
        return await w._result.Task as Color?;
    }

    private void UpdateDim(Rect hole)
    {
        Geometry full = new RectangleGeometry(new Rect(0, 0, Width, Height));
        _dim.Data = hole.IsEmpty
            ? full
            : new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(hole, 6, 6));
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (_mode == Mode.Color)
        {
            Finish(PixelAt(e.GetPosition(_canvas)));
            return;
        }
        _start = e.GetPosition(_canvas);
        CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(_canvas);

        if (_mode == Mode.Color)
        {
            const double zoomPx = 15;
            _loupeBrush.Viewbox = new Rect(p.X * _scale - zoomPx / 2, p.Y * _scale - zoomPx / 2, zoomPx, zoomPx);
            Canvas.SetLeft(_loupe, p.X + 18);
            Canvas.SetTop(_loupe, p.Y + 18);
            var c = PixelAt(p);
            _labelText.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            _label.Background = new SolidColorBrush(c);
            _labelText.Foreground = (c.R * 0.3 + c.G * 0.59 + c.B * 0.11) > 140 ? Brushes.Black : Brushes.White;
            _label.Visibility = Visibility.Visible;
            Canvas.SetLeft(_label, p.X + 40);
            Canvas.SetTop(_label, p.Y + 152);
            return;
        }

        if (_start is not Point s) return;
        var r = new Rect(s, p);
        _frame.Visibility = Visibility.Visible;
        _frame.Width = r.Width;
        _frame.Height = r.Height;
        Canvas.SetLeft(_frame, r.X);
        Canvas.SetTop(_frame, r.Y);
        UpdateDim(r);

        _labelText.Text = $"{Math.Round(r.Width * _scale)} × {Math.Round(r.Height * _scale)}";
        _label.Visibility = Visibility.Visible;
        Canvas.SetLeft(_label, r.X);
        Canvas.SetTop(_label, Math.Max(0, r.Y - 30));
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_mode == Mode.Color || _start is not Point s) return;
        ReleaseMouseCapture();
        var r = new Rect(s, e.GetPosition(_canvas));
        if (r.Width < 4 || r.Height < 4)
        {
            Finish(null);
            return;
        }

        var px = new Int32Rect(
            (int)Math.Round(r.X * _scale), (int)Math.Round(r.Y * _scale),
            (int)Math.Round(r.Width * _scale), (int)Math.Round(r.Height * _scale));
        px.Width = Math.Min(px.Width, _desktop.PixelWidth - px.X);
        px.Height = Math.Min(px.Height, _desktop.PixelHeight - px.Y);
        Finish((CaptureService.Crop(_desktop, px), px));
    }

    private Color PixelAt(Point p)
    {
        int x = Math.Clamp((int)(p.X * _scale), 0, _desktop.PixelWidth - 1);
        int y = Math.Clamp((int)(p.Y * _scale), 0, _desktop.PixelHeight - 1);
        var one = new FormatConvertedBitmap(new CroppedBitmap(_desktop, new Int32Rect(x, y, 1, 1)), PixelFormats.Bgra32, null, 0);
        var px = new byte[4];
        one.CopyPixels(px, 4, 0);
        return Color.FromRgb(px[2], px[1], px[0]);
    }

    private void Finish(object? value)
    {
        _result.TrySetResult(value);
        Close();
    }
}

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// "Apps" row at the bottom of Home: drop programs, shortcuts, files or folders onto it, click to open.
/// Icons grow with a spring under the cursor; a dot shows which apps are running.
/// </summary>
public partial class MainWindow
{
    private readonly Dictionary<string, (Border Dot, string? Exe)> _appDots = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _appsTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private const double AppIcon = 38;

    private void InitApps()
    {
        _appsTimer.Tick += (_, _) => UpdateRunningApps();
        HomePage.IsVisibleChanged += (_, _) => { if (HomePage.IsVisible) { UpdateRunningApps(); _appsTimer.Start(); } else _appsTimer.Stop(); };
        BuildApps();
        // Store apps get their names once the index has loaded
        _ = _apps.EnsureLoadedAsync().ContinueWith(_ => Dispatcher.InvokeAsync(BuildApps));
    }

    private void BuildApps()
    {
        AppsRow.Children.Clear();
        _appDots.Clear();
        _settings.DockApps.RemoveAll(p => !AppIndex.IsStoreApp(p) && !File.Exists(p) && !Directory.Exists(p));
        foreach (var path in _settings.DockApps) AppsRow.Children.Add(MakeAppItem(path));
        AppsRow.Children.Add(MakePlusButton());
        if (_settings.DockApps.Count == 0)
        {
            var hint = new TextBlock
            {
                Text = L.T("Добавьте приложения кнопкой «+» — они будут открываться в один клик"),
                Style = (Style)FindResource("Muted"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 8),
            };
            AppsRow.Children.Add(hint);
        }
        UpdateRunningApps();
        RefitIsland();
    }

    /// <summary>The open island grows or shrinks to fit the Home page after its content changed.</summary>
    private void RefitIsland()
    {
        if (_mode != Mode.Expanded) return;
        Dispatcher.BeginInvoke(() => { if (_mode == Mode.Expanded) MorphTo(TargetSize(Mode.Expanded)); }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private FrameworkElement MakePlusButton()
    {
        var plus = new TextBlock
        {
            Text = "\uE710", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        plus.SetResourceReference(TextBlock.ForegroundProperty, "Fg2");
        var circle = new Border
        {
            Width = AppIcon, Height = AppIcon, CornerRadius = new CornerRadius(AppIcon / 2), Child = plus, Cursor = Cursors.Hand,
            Margin = new Thickness(6, 2, 6, 8), ToolTip = L.T("Добавить приложение"), VerticalAlignment = VerticalAlignment.Top,
        };
        circle.SetResourceReference(Border.BackgroundProperty, "ChipBg");
        circle.MouseEnter += (_, _) => circle.SetResourceReference(Border.BackgroundProperty, "TileBgHover");
        circle.MouseLeave += (_, _) => circle.SetResourceReference(Border.BackgroundProperty, "ChipBg");
        circle.MouseLeftButtonUp += async (_, e) =>
        {
            e.Handled = true;
            await ToggleAppSearch(AppSearchPanel.Visibility != Visibility.Visible);
        };
        return circle;
    }

    // ------------------------------------------------------------------ search by name

    private async Task ToggleAppSearch(bool open)
    {
        AppSearchPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        _pinnedOpen = open;   // don't fold away while typing
        AppSearchResults.Children.Clear();
        AppSearchBox.Text = "";
        RefitIsland();
        if (!open) return;
        Activate();
        AppSearchBox.Focus();
        Keyboard.Focus(AppSearchBox);
        await _apps.EnsureLoadedAsync();
        ShowAppResults("");
    }

    private void AppSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        AppSearchPlaceholder.Visibility = AppSearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowAppResults(AppSearchBox.Text);
    }

    private async void AppSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; await ToggleAppSearch(false); }
        else if (e.Key == Key.Enter && AppSearchResults.Children.Count > 0 && AppSearchResults.Children[0] is Button first)
        {
            e.Handled = true;
            first.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        }
    }

    private void ShowAppResults(string q)
    {
        AppSearchResults.Children.Clear();
        var found = _apps.Search(q, 12)
            .Where(a => !_settings.DockApps.Contains(a.Path, StringComparer.OrdinalIgnoreCase))
            .Take(8).ToList();
        if (q.Trim().Length > 0 && found.Count == 0)
        {
            AppSearchResults.Children.Add(new TextBlock { Text = L.T("Ничего не найдено"), Style = (Style)FindResource("Muted"), Margin = new Thickness(4, 2, 0, 0) });
            RefitIsland();
            return;
        }
        foreach (var app in found)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = _apps.Icon(app.Path);
            if (icon != null) content.Children.Add(new Image { Source = icon, Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0) });
            content.Children.Add(new TextBlock { Text = app.Name, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 170, TextTrimming = TextTrimming.CharacterEllipsis });
            var b = new Button { Style = (Style)FindResource("GhostPill"), Content = content, Padding = new Thickness(10, 6, 12, 6), Margin = new Thickness(0, 0, 6, 6) };
            var path = app.Path;
            b.Click += async (_, _) =>
            {
                AddApps([path]);
                await ToggleAppSearch(false);
            };
            AppSearchResults.Children.Add(b);
        }
        RefitIsland();
    }

    private bool OverApps(DragEventArgs e)
    {
        if (!HomeApps.IsVisible) return false;
        var p = e.GetPosition(HomeApps);
        return p.X >= 0 && p.Y >= 0 && p.X <= HomeApps.ActualWidth && p.Y <= HomeApps.ActualHeight;
    }

    private void AddApps(IEnumerable<string> files)
    {
        int added = 0;
        foreach (var f in files)
            if (!_settings.DockApps.Contains(f, StringComparer.OrdinalIgnoreCase)) { _settings.DockApps.Add(f); added++; }
        _settings.Save();
        BuildApps();
        if (added > 0) Kick(2);
    }

    private FrameworkElement MakeAppItem(string path)
    {
        var name = AppIndex.IsStoreApp(path)
            ? _apps.NameOf(path) ?? "App"
            : Path.GetFileNameWithoutExtension(path.TrimEnd('\\'));
        var img = new Image { Width = AppIcon, Height = AppIcon, Source = ShellIcons.Get(path, 96) ?? SmallAppIcon(path) };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

        var scale = new ScaleTransform(1, 1);
        var lift = new TranslateTransform();
        img.RenderTransformOrigin = new Point(0.5, 0.9);
        img.RenderTransform = new TransformGroup { Children = { scale, lift } };

        var dot = new Border { Width = 4, Height = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 4, 0, 0), Opacity = 0 };
        dot.SetResourceReference(Border.BackgroundProperty, "Fg");
        var stack = new StackPanel { Width = 50 };
        stack.Children.Add(img);
        stack.Children.Add(dot);
        var cell = new Border { Background = Brushes.Transparent, Child = stack, Cursor = Cursors.Hand, ToolTip = name, Padding = new Thickness(0, 2, 0, 0) };

        void Spring(double s, double y)
        {
            var ease = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut };
            var d = TimeSpan.FromMilliseconds(260);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(s, d) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(s, d) { EasingFunction = ease });
            lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, d) { EasingFunction = ease });
        }
        cell.MouseEnter += (_, _) => Spring(1.22, -3);
        cell.MouseLeave += (_, _) => Spring(1, 0);
        cell.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Spring(0.86, 0);
            Dispatcher.InvokeAsync(async () => { await Task.Delay(120); Spring(1.22, -3); });
            AppIndex.Launch(path);
        };

        var menu = new ContextMenu();
        var open = new MenuItem { Header = L.T("Открыть") };
        open.Click += (_, _) => AppIndex.Launch(path);
        var folder = new MenuItem { Header = L.T("Показать в папке") };
        folder.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        var remove = new MenuItem { Header = L.T("Убрать из приложений") };
        remove.Click += (_, _) =>
        {
            _settings.DockApps.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
            _settings.Save();
            BuildApps();
        };
        menu.Items.Add(open);
        if (!AppIndex.IsStoreApp(path)) menu.Items.Add(folder);
        menu.Items.Add(new Separator());
        menu.Items.Add(remove);
        cell.ContextMenu = menu;

        _appDots[path] = (dot, TargetExe(path));
        return cell;
    }

    private static ImageSource? SmallAppIcon(string path)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon == null) return null;
            var s = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            s.Freeze();
            return s;
        }
        catch { return null; }
    }

    /// <summary>The program behind a shortcut, to tell whether it is running.</summary>
    private static string? TargetExe(string path)
    {
        try
        {
            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return Path.GetFileNameWithoutExtension(path);
            if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return null;
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return null;
            dynamic shell = Activator.CreateInstance(shellType)!;
            var target = (string)shell.CreateShortcut(path).TargetPath;
            return target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(target) : null;
        }
        catch { return null; }
    }

    private void UpdateRunningApps()
    {
        if (_appDots.Count == 0) return;
        HashSet<string> running;
        try { running = Process.GetProcesses().Select(p => p.ProcessName).ToHashSet(StringComparer.OrdinalIgnoreCase); }
        catch { return; }
        foreach (var (dot, exe) in _appDots.Values)
        {
            double to = exe != null && running.Contains(exe) ? 0.8 : 0;
            if (Math.Abs(dot.Opacity - to) > 0.01) dot.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(300)));
        }
    }
}

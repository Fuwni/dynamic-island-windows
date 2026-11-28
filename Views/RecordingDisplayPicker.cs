using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using DynamicIsland.Services;

namespace DynamicIsland.Views;

/// <summary>Every recording starts with an explicit display choice. Closing cancels.</summary>
public sealed class RecordingDisplayPicker : Window
{
    public string? SelectedDeviceName { get; private set; }

    public RecordingDisplayPicker(bool dark = true)
    {
        Title = L.T("Выберите экран для записи");
        Width = 560;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 620;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        SetResourceReference(ForegroundProperty, "Fg");

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock
        {
            Text = Title, FontSize = 18, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
        });
        var hint = new TextBlock
        {
            Text = L.T("Нажмите на экран, чтобы начать запись."),
            FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18),
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Fg2");
        panel.Children.Add(hint);
        var screens = new StackPanel();
        var names = DisplayNames.GetActive();
        int index = 0;
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var number = screen.DeviceName.Replace(@"\\.\DISPLAY", "");
            var label = new StackPanel();
            label.Children.Add(new TextBlock
            {
                Text = names.TryGetValue(screen.DeviceName, out var name) ? name : L.F("Экран {0}", number),
                FontSize = 13.5, FontWeight = FontWeights.SemiBold,
                MaxWidth = 360, TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var details = new TextBlock
            {
                Text = L.F("Экран {0}", number) + $" · {screen.Bounds.Width} × {screen.Bounds.Height}" + (screen.Primary ? " · " + L.T("основной") : ""),
                FontSize = 11.5, Margin = new Thickness(0, 3, 0, 0),
            };
            details.SetResourceReference(TextBlock.ForegroundProperty, "Fg2");
            label.Children.Add(details);
            var button = new Button
            {
                Content = label, Tag = "\uE7F4",
                Style = (Style)FindResource("WideAction"), Height = 74,
                Background = index++ % 2 == 0
                    ? new LinearGradientBrush(Color.FromRgb(0x3A, 0x9B, 0xFF), Color.FromRgb(0x0A, 0x6C, 0xE0), 90)
                    : new LinearGradientBrush(Color.FromRgb(0x8A, 0x7A, 0xFF), Color.FromRgb(0x5B, 0x4B, 0xD8), 90),
                Focusable = true,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(4, 0, 4, 10),
                Padding = new Thickness(14, 12, 14, 12),
            };
            button.Click += (_, _) => { SelectedDeviceName = screen.DeviceName; DialogResult = true; };
            screens.Children.Add(button);
        }
        panel.Children.Add(new ScrollViewer
        {
            Content = screens, MaxHeight = 390,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });
        var cancel = new Button
        {
            Content = L.T("Отмена"), IsCancel = true,
            Focusable = true,
            Style = (Style)FindResource("Pill"),
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0),
        };
        cancel.Click += (_, _) => DialogResult = false;
        cancel.SetResourceReference(Button.BackgroundProperty, "TileBg");
        cancel.SetResourceReference(Button.BorderBrushProperty, "TileStroke");
        panel.Children.Add(cancel);
        var card = new Border
        {
            Child = panel, CornerRadius = new CornerRadius(30), Margin = new Thickness(24),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Opacity = 0.45, Direction = 270 },
        };
        card.Background = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1),
            GradientStops = dark
                ? new GradientStopCollection { new((Color)ColorConverter.ConvertFromString("#222C3D"), 0), new((Color)ColorConverter.ConvertFromString("#12151D"), 0.35), new((Color)ColorConverter.ConvertFromString("#171920"), 1) }
                : new GradientStopCollection { new((Color)ColorConverter.ConvertFromString("#F6F8FF"), 0), new((Color)ColorConverter.ConvertFromString("#E8ECF4"), 1) },
        };
        card.BorderBrush = new LinearGradientBrush(
            dark ? Color.FromRgb(0x70, 0x7D, 0x91) : Colors.White,
            dark ? Color.FromRgb(0x38, 0x3C, 0x46) : Color.FromRgb(0xB8, 0xC0, 0xCE), 90);
        Content = card;
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// A Discord call as its own little island to the right of the main one: the main island keeps showing
/// the music, the time or a timer, and the call (who's talking, your microphone, how long) sits beside it.
/// A click on it brings Discord forward, the microphone button mutes you.
/// </summary>
public partial class MainWindow
{
    private static readonly Color DiscordBlurple = Color.FromRgb(0x58, 0x65, 0xF2);
    private Canvas? _sideLayer;
    private Border? _callPill, _callBadge, _callTalkRing;
    private TextBlock? _callTime;
    private Button? _callMic;
    private DateTime _callSince;
    private bool _callShown, _callWasOn;

    private bool DiscordBeside => _settings.DiscordSidePill;

    /// <summary>Every 250 ms (from UpdateLive): show, update or hide the call island.</summary>
    private void UpdateDiscordPill()
    {
        bool want = _discordInCall && DiscordBeside && !_hidden && !_fullscreenHidden && !_recording.IsRecording && IsLoaded;
        // the call's length counts from when the call started, not from when the little island last appeared
        if (_discordInCall && !_callWasOn) _callSince = DateTime.Now;
        _callWasOn = _discordInCall;
        if (!want)
        {
            if (_callShown) FadeCallPill(false);
            return;
        }
        EnsureCallPill();
        var len = DateTime.Now - _callSince;
        _callTime!.Text = len.TotalHours >= 1 ? len.ToString(@"h\:mm\:ss") : len.ToString(@"m\:ss");
        bool muted = _audio.MicMuted == true;
        _callMic!.Content = muted ? "" : "";
        _callMic.Foreground = muted ? new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)) : (Brush)FindResource("Fg");
        _callMic.ToolTip = muted ? L.T("Включить микрофон") : L.T("Выключить микрофон");
        _callTalkRing!.Opacity = _discordSpeaking ? 0.55 + 0.45 * Math.Sin(_tickCount * 0.9) : 0;

        if (!PlaceBeside(_callPill!, 0)) { if (_callShown) FadeCallPill(false); return; }
        if (!_callShown) FadeCallPill(true);
    }

    private void EnsureCallPill()
    {
        if (_callPill != null) return;
        _sideLayer = new Canvas();
        Root.Children.Add(_sideLayer);

        var icon = new TextBlock { Text = "", FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 13, Foreground = Brushes.White,
                                   HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _callTalkRing = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), BorderThickness = new Thickness(2),
                                     BorderBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0xD2, 0x6B)), Opacity = 0, IsHitTestVisible = false };
        _callBadge = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(DiscordBlurple), Child = icon };
        var badgeHost = new Grid { Width = 30, Height = 30, Margin = new Thickness(0, 0, 6, 0) };
        badgeHost.Children.Add(_callTalkRing);
        badgeHost.Children.Add(_callBadge);

        _callTime = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, MinWidth = 34 };
        _callTime.SetResourceReference(TextBlock.ForegroundProperty, "Fg");

        _callMic = new Button { Style = (Style)FindResource("IconButton"), Width = 26, Height = 26, FontSize = 12, Margin = new Thickness(6, 0, 0, 0) };
        _callMic.Click += (_, e) => { _audio.MicMuted = !(_audio.MicMuted == true); e.Handled = true; UpdateDiscordPill(); };

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(badgeHost); row.Children.Add(_callTime); row.Children.Add(_callMic);
        _callPill = new Border
        {
            CornerRadius = new CornerRadius(20), Padding = new Thickness(3, 3, 6, 3), Child = row, Opacity = 0, Cursor = Cursors.Hand,
            BorderThickness = new Thickness(1), ToolTip = L.T("Звонок в Discord · нажмите, чтобы открыть"),
            RenderTransformOrigin = new Point(0, 0.5), RenderTransform = new ScaleTransform(0.6, 0.6), Visibility = Visibility.Collapsed,
        };
        _callPill.SetResourceReference(Border.BackgroundProperty, "IslandBg");
        _callPill.SetResourceReference(Border.BorderBrushProperty, "GlassRim");
        _callPill.MouseLeftButtonUp += (_, e) => { if (!e.Handled) FocusApp("Discord"); };
        _sideLayer.Children.Add(_callPill);
    }

    private void FadeCallPill(bool show)
    {
        if (_callPill == null) return;
        _callShown = show;
        if (show) _callPill.Visibility = Visibility.Visible;
        var fade = new DoubleAnimation(show ? 1 : 0, TimeSpan.FromMilliseconds(show ? 200 : 160));
        if (!show) fade.Completed += (_, _) => { if (!_callShown) _callPill.Visibility = Visibility.Collapsed; };
        _callPill.BeginAnimation(OpacityProperty, fade);
        var pop = new DoubleAnimation(show ? 1 : 0.7, TimeSpan.FromMilliseconds(show ? 380 : 160))
        { EasingFunction = show ? new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut } : null };
        ((ScaleTransform)_callPill.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        ((ScaleTransform)_callPill.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    /// <summary>
    /// Put a little island to the right of the main one, level with its top (the folded pill's height).
    /// skip: room already taken by other side islands. False when there's no room (a wide open island).
    /// </summary>
    private bool PlaceBeside(FrameworkElement e, double skip)
    {
        e.LayoutTransform = Island.LayoutTransform;   // same size setting as the island
        e.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = e.DesiredSize;
        var tl = Island.TranslatePoint(new Point(0, 0), Root);
        double iw = Island.ActualWidth * UiScale;
        double x = tl.X + iw + 10 + skip;
        if (x + size.Width > Root.ActualWidth - 4) return false;
        double pillH = 36 * UiScale;
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, tl.Y + Math.Max(0, (pillH - size.Height) / 2));
        return true;
    }

    /// <summary>Width the call island takes beside the main one (so the Caps Lock pill goes after it).</summary>
    private double SideTaken => _callShown && _callPill != null ? _callPill.DesiredSize.Width + 8 : 0;
}

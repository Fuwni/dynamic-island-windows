using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DynamicIsland.Services;
using Drawing = System.Drawing;

namespace DynamicIsland;

/// <summary>
/// Frosted glass, the see-through lower half, the shimmering colour aura,
/// page dots in the header and the settings sidebar.
/// </summary>
public partial class MainWindow
{
    private readonly DispatcherTimer _frostTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private const double FrostHalfWidth = 380, FrostHeight = 600;
    private LinearGradientBrush? _auraBrush;

    public static readonly (string Id, string Name, string[] Colors)[] Auras =
    [
        ("none", "Нет", []),
        ("aurora", "Аврора", ["#5B8CFF", "#A066FF", "#3DE1FF"]),
        ("sunset", "Закат", ["#FF6B6B", "#FFB347", "#FF5E9C"]),
        ("ocean", "Океан", ["#00C6FF", "#0072FF", "#00E0B0"]),
        ("neon", "Неон", ["#39FF14", "#00F0FF", "#FF00E5"]),
        ("mono", "Иней", ["#FFFFFF", "#8E8E93", "#D1D1D6"]),
    ];

    private void InitGlass()
    {
        _frostTimer.Tick += (_, _) => CaptureFrost();
        _frostTimer.Start();
        ApplyGlass();
        SetupAura();
        UpdatePageDots();
        ApplyTabIconsMode();
    }

    // ================================================================ fill: tint + transparent bottom

    /// <summary>The island's fill: the theme colour, see-through over the frost, fading at the bottom.</summary>
    private void ApplyGlass()
    {
        var c = (FindResource("IslandBg") as SolidColorBrush)?.Color ?? Colors.Black;
        c.A = 255;
        bool expanded = _mode != Mode.Compact;
        double tint = _settings.Glass ? Math.Clamp(_settings.GlassTint, 20, 100) / 100.0 : 1;
        // the folded pill stays readable: never thinner than 80 %
        double body = expanded ? tint : Math.Max(tint, 0.8);
        double bottom = _settings.BottomFade ? body * (expanded ? 0.55 : 0.45) : body;

        Color A(double a) => Color.FromArgb((byte)Math.Round(Math.Clamp(a, 0, 1) * 255), c.R, c.G, c.B);
        var fill = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        fill.GradientStops.Add(new GradientStop(A(1), 0));          // the top meets the screen edge: solid
        fill.GradientStops.Add(new GradientStop(A(body), 0.14));
        fill.GradientStops.Add(new GradientStop(A(body), 0.5));
        fill.GradientStops.Add(new GradientStop(A(bottom), 1));
        fill.Freeze();
        IslandShape.Fill = fill;

        ApplyCaptureExclusion();
        FrostHost.Visibility = _settings.Glass && !_dragging ? Visibility.Visible : Visibility.Collapsed;
        if (_settings.Glass) CaptureFrost();
        UpdateAuraVisibility();
    }

    private bool? _excluded;

    /// <summary>
    /// With frosted glass on, the island is left out of every screen capture - otherwise it would photograph
    /// itself and the glass turns opaque. With glass off it shows up in screenshots and recordings again.
    /// </summary>
    private void ApplyCaptureExclusion()
    {
        if (_hwnd == IntPtr.Zero || _excluded == _settings.Glass) return;
        _excluded = _settings.Glass;
        if (!Native.SetWindowDisplayAffinity(_hwnd, _settings.Glass ? Native.WDA_EXCLUDEFROMCAPTURE : 0))
            Log.Write("display affinity: not supported");
    }

    /// <summary>Snapshot of the screen behind the island (our own window isn't in it), blurred by the Image's effect.</summary>
    private void CaptureFrost()
    {
        if (!_settings.Glass || _hidden || _fullscreenHidden || !IsLoaded || !Island.IsVisible) return;
        if (_dragging || _posActive) { FrostHost.Opacity = 0; return; }
        try
        {
            var top = Island.PointToScreen(new Point(Island.ActualWidth / 2, 0));
            double dpi = Dpi;
            var rect = new Drawing.Rectangle(
                (int)(top.X - FrostHalfWidth * dpi), (int)top.Y,
                (int)(FrostHalfWidth * 2 * dpi), (int)(FrostHeight * dpi));
            // plain BitBlt leaves layered windows (the island itself) out of the picture
            Frost.Source = CaptureService.CaptureRect(rect);
            Frost.Width = FrostHalfWidth * 2;
            Frost.Height = FrostHeight;
            if (FrostHost.Opacity < 1) FrostHost.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(250)));
        }
        catch { }
    }

    /// <summary>Called whenever the outline changes: keep frost and aura glued to the shape.</summary>
    private void ShapeChanged(Geometry g, double width)
    {
        FrostHost.Clip = g;
        Canvas.SetLeft(Frost, width / 2 - FrostHalfWidth);
        Canvas.SetTop(Frost, 0);
        AuraHost.Clip = g;
        AuraFill.Width = width;
        AuraFill.Height = Island.Height + 12;
        AuraLine.Data = g;
        AuraLine.Clip = g;   // only the inner half of the edge line shows: it's part of the island
    }

    // ================================================================ aura

    private void SetupAura()
    {
        var aura = Auras.FirstOrDefault(a => a.Id == _settings.Aura);
        // the Claude Code character brings its own warm orange light
        if (_settings.Character is "claude" or "clawd" && _settings.ClaudeGlow) aura = ("claude", "Claude", ["#D97757", "#F0A27A", "#C2552F"]);
        if (aura.Colors == null || aura.Colors.Length == 0)
        {
            _auraBrush = null;
            UpdateAuraVisibility();
            return;
        }

        // colours repeat so the band can slide forever without a seam
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(0.5, 0.5), SpreadMethod = GradientSpreadMethod.Repeat };
        var cols = aura.Colors.Append(aura.Colors[0]).ToArray();
        for (int i = 0; i < cols.Length; i++)
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(cols[i]), (double)i / (cols.Length - 1)));
        var shift = new TranslateTransform();
        brush.RelativeTransform = shift;
        shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 0.5, TimeSpan.FromSeconds(4)) { RepeatBehavior = RepeatBehavior.Forever });
        _auraBrush = brush;

        // light rises from the bottom edge and dies out before the middle
        var mask = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        mask.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
        mask.GradientStops.Add(new GradientStop(Colors.Transparent, 0.5));
        mask.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, 0, 0, 0), 0.85));
        mask.GradientStops.Add(new GradientStop(Color.FromArgb(0xB0, 0, 0, 0), 1));
        mask.Freeze();
        var lineMask = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        lineMask.GradientStops.Add(new GradientStop(Colors.Transparent, 0.45));
        lineMask.GradientStops.Add(new GradientStop(Colors.Black, 1));
        lineMask.Freeze();

        AuraFill.Fill = brush;
        AuraFill.OpacityMask = mask;
        AuraLine.Stroke = brush;
        AuraLine.OpacityMask = lineMask;
        // slow breathing
        AuraFill.BeginAnimation(OpacityProperty, new DoubleAnimation(0.45, 0.9, TimeSpan.FromSeconds(3))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
        UpdateAuraVisibility();
    }

    private bool _auraThinking;

    /// <summary>While Claude Code waits for you, its orange light breathes faster.</summary>
    private void UpdateAuraMood(bool thinking)
    {
        if (_settings.Character is not ("claude" or "clawd") || !_settings.ClaudeGlow || thinking == _auraThinking) return;
        _auraThinking = thinking;
        AuraFill.BeginAnimation(OpacityProperty, new DoubleAnimation(thinking ? 0.5 : 0.45, thinking ? 1 : 0.9, TimeSpan.FromSeconds(thinking ? 0.8 : 3))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
    }

    private void UpdateAuraVisibility()
    {
        bool show = _auraBrush != null && _mode == Mode.Compact && !_recording.IsRecording;
        var v = show ? Visibility.Visible : Visibility.Collapsed;
        if (AuraLine.Visibility == v) return;
        AuraLine.Visibility = AuraHost.Visibility = v;
        if (show) AuraHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(700)));
    }

    // ================================================================ header: page dots

    private RadioButton[] PageTabs => [TabHome, TabMusic, TabWeather, TabChat, TabTimer, TabClip, TabNotes, TabCalendar, TabSystem];
    private bool _iconsShown;

    private void UpdatePageDots()
    {
        if (DotsRow == null) return;
        if (DotsRow.Children.Count == 0)
        {
            foreach (var tab in PageTabs)
            {
                var dot = new Border { Height = 6, Width = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 5, 0), Cursor = Cursors.Hand, Tag = tab };
                var t = tab;
                dot.MouseLeftButtonUp += (_, _) => t.IsChecked = true;
                DotsRow.Children.Add(dot);
            }
        }
        var current = PageTabs.FirstOrDefault(t => t.IsChecked == true);
        foreach (Border dot in DotsRow.Children)
        {
            bool on = dot.Tag == current;
            dot.SetResourceReference(Border.BackgroundProperty, on ? "Fg" : "Fg3");
            dot.BeginAnimation(WidthProperty, new DoubleAnimation(on ? 18 : 6, TimeSpan.FromMilliseconds(320)) { EasingFunction = new SpringEase { DampingRatio = 0.6 } });
        }
        PageTitle.Text = current?.Content as string ?? (TabSettings.IsChecked == true ? "Настройки" : "");
    }

    private void ApplyTabIconsMode()
    {
        bool always = _settings.TabIcons == "always";
        ShowTabIcons(always, animate: false);
    }

    private readonly DispatcherTimer _iconsHide = new() { Interval = TimeSpan.FromMilliseconds(160) };

    private void TabArea_MouseEnter(object sender, MouseEventArgs e)
    {
        _iconsHide.Stop();
        if (_settings.TabIcons != "always") ShowTabIcons(true, animate: true);
    }

    private void TabArea_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_settings.TabIcons == "always") return;
        // a short grace period: crossing a gap between icons must not flicker the strip
        _iconsHide.Tick -= HideIconsTick;
        _iconsHide.Tick += HideIconsTick;
        _iconsHide.Start();
    }

    private void HideIconsTick(object? sender, EventArgs e)
    {
        _iconsHide.Stop();
        if (!TabArea.IsMouseOver) ShowTabIcons(false, animate: true);
    }

    /// <summary>Dots melt into icons one after another (and back) - opacity, scale and a little drift together.</summary>
    private void ShowTabIcons(bool show, bool animate)
    {
        if (_iconsShown == show && animate) return;
        _iconsShown = show;
        TabStrip.IsHitTestVisible = show;
        TabStrip.Opacity = 1;
        TabStripShift.X = 0;

        var tabs = PageTabs;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var soft = new SineEase { EasingMode = EasingMode.EaseInOut };
        for (int i = 0; i < tabs.Length; i++)
        {
            var tab = tabs[i];
            if (tab.RenderTransform is not ScaleTransform st || st.IsFrozen)
            {
                st = new ScaleTransform(1, 1);
                tab.RenderTransform = st;
                tab.RenderTransformOrigin = new Point(0.5, 0.5);
            }
            int order = show ? i : tabs.Length - 1 - i;
            var begin = TimeSpan.FromMilliseconds(animate ? order * (show ? 16 : 10) : 0);
            var dur = TimeSpan.FromMilliseconds(animate ? (show ? 300 : 200) : 0);
            tab.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 1 : 0, dur) { BeginTime = begin, EasingFunction = soft });
            st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(show ? 1 : 0.55, dur) { BeginTime = begin, EasingFunction = ease });
            st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(show ? 1 : 0.55, dur) { BeginTime = begin, EasingFunction = ease });
        }

        // dots shrink away in the same rhythm, the page name slides aside
        for (int i = 0; i < DotsRow.Children.Count; i++)
        {
            var dot = (UIElement)DotsRow.Children[i];
            int order = show ? i : DotsRow.Children.Count - 1 - i;
            dot.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 0 : 1, TimeSpan.FromMilliseconds(animate ? 220 : 0))
            {
                BeginTime = TimeSpan.FromMilliseconds(animate ? order * 12 + (show ? 0 : 90) : 0), EasingFunction = soft,
            });
        }
        PageTitle.BeginAnimation(OpacityProperty, new DoubleAnimation(show ? 0 : 1, TimeSpan.FromMilliseconds(animate ? 240 : 0))
        {
            BeginTime = TimeSpan.FromMilliseconds(show || !animate ? 0 : 140), EasingFunction = soft,
        });
    }

    // ================================================================ settings: sidebar, look controls

    private void BuildSettingsNav()
    {
        bool first = true;
        foreach (StackPanel section in SettingsSections.Children)
        {
            var parts = ((string)section.Tag).Split('|');
            var nav = new RadioButton { Style = (Style)FindResource("SideNav"), GroupName = "SettingsNav", Content = parts[0], Tag = parts[1] };
            var sec = section;
            nav.Checked += (_, _) =>
            {
                foreach (UIElement s in SettingsSections.Children) s.Visibility = Visibility.Collapsed;
                SettingsScroll.ScrollToTop();
                Reveal(sec, 0.99, 0, 0);
            };
            SettingsNav.Children.Add(nav);
            if (first) { nav.IsChecked = true; first = false; }
        }
    }

    private void BuildAuraChips()
    {
        foreach (var (id, name, colors) in Auras)
        {
            var dot = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 7, 0) };
            if (colors.Length > 0)
            {
                var g = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(colors[0]), (Color)ColorConverter.ConvertFromString(colors[^1]), 0);
                g.GradientStops.Insert(1, new GradientStop((Color)ColorConverter.ConvertFromString(colors[1]), 0.5));
                dot.Background = g;
            }
            else
            {
                dot.BorderThickness = new Thickness(1.2);
                dot.SetResourceReference(Border.BorderBrushProperty, "Fg3");
            }
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(dot);
            content.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            var chip = new RadioButton { Style = (Style)FindResource("Segment"), GroupName = "Aura", Content = content, Tag = id, Margin = new Thickness(0, 0, 4, 4) };
            chip.Checked += (_, _) =>
            {
                if (_settingsLoading) return;
                _settings.Aura = id;
                _settings.Save();
                SetupAura();
            };
            AuraChips.Children.Add(chip);
        }
    }

    private void RefreshLookControls()
    {
        foreach (var card in new[] { StyleDocked, StyleFloating })
        {
            bool on = ((string)card.Tag == "dock") == _settings.Docked;
            if (on) card.SetResourceReference(Border.BorderBrushProperty, "AccentBlue");
            else card.BorderBrush = Brushes.Transparent;
        }
        GlassSwitch.IsChecked = _settings.Glass;
        TintRow.Visibility = _settings.Glass ? Visibility.Visible : Visibility.Collapsed;
        TintLabel.Text = $"{_settings.GlassTint}%";
        FadeSwitch.IsChecked = _settings.BottomFade;
        BounceSwitch.IsChecked = _settings.Bouncy;
        (_settings.FastAnimations ? SpeedFast : SpeedNormal).IsChecked = true;
        (_settings.TabIcons == "always" ? IconsAlways : IconsHover).IsChecked = true;
        foreach (RadioButton chip in AuraChips.Children)
            chip.IsChecked = (string)chip.Tag == _settings.Aura;
    }

    private void StyleCard_Click(object sender, MouseButtonEventArgs e)
    {
        _settings.Docked = (string)((FrameworkElement)sender).Tag == "dock";
        ApplyDock(animate: true);
        SavePosition();
        RefreshLookControls();
        PlayChromatic(5);
    }

    private void Glass_Click(object sender, RoutedEventArgs e)
    {
        _settings.Glass = GlassSwitch.IsChecked == true;
        _settings.Save();
        ApplyGlass();
        RefreshLookControls();
        Remeasure();
    }

    private void TintMinus_Click(object sender, RoutedEventArgs e) => ChangeTint(-5);
    private void TintPlus_Click(object sender, RoutedEventArgs e) => ChangeTint(5);

    private void ChangeTint(int delta)
    {
        _settings.GlassTint = Math.Clamp(_settings.GlassTint + delta, 20, 100);
        _settings.Save();
        TintLabel.Text = $"{_settings.GlassTint}%";
        ApplyGlass();
    }

    private void Fade_Click(object sender, RoutedEventArgs e)
    {
        _settings.BottomFade = FadeSwitch.IsChecked == true;
        _settings.Save();
        ApplyGlass();
    }

    private void Bounce_Click(object sender, RoutedEventArgs e)
    {
        _settings.Bouncy = BounceSwitch.IsChecked == true;
        _settings.Save();
        if (_settings.Bouncy) Reveal(SettingsSections, 0.97, 0, 0); // show what it feels like
    }

    private void Speed_Checked(object sender, RoutedEventArgs e)
    {
        if (_settingsLoading || SpeedFast == null) return;
        _settings.FastAnimations = SpeedFast.IsChecked == true;
        _settings.Save();
    }

    private void Icons_Checked(object sender, RoutedEventArgs e)
    {
        if (_settingsLoading || IconsAlways == null) return;
        _settings.TabIcons = IconsAlways.IsChecked == true ? "always" : "hover";
        _settings.Save();
        ApplyTabIconsMode();
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>Settings tab.</summary>
public partial class MainWindow
{
    private bool _settingsLoading;

    private void InitSettingsPage()
    {
        foreach (var hex in Theme.Palette)
        {
            var b = new Button
            {
                Style = (Style)FindResource("Swatch"),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                ToolTip = hex,
            };
            b.Click += (_, _) => SetIslandColor(hex);
            SwatchPanel.Children.Add(b);
        }

        foreach (var (id, _, label) in AllActions)
        {
            var chip = new CheckBox { Style = (Style)FindResource("ChipToggle"), Content = label, Tag = id };
            chip.Click += (_, _) =>
            {
                if (chip.IsChecked == true)
                {
                    if (!_settings.Actions.Contains(id))
                    {
                        // keep the original order
                        var order = AllActions.Select(a => a.Id).ToList();
                        _settings.Actions.Add(id);
                        _settings.Actions.Sort((a, b) => order.IndexOf(a).CompareTo(order.IndexOf(b)));
                    }
                }
                else _settings.Actions.Remove(id);
                _settings.Save();
                BuildHome();
            };
            ActionChips.Children.Add(chip);
        }
        RefreshSettingsPage();
    }

    private void RefreshSettingsPage()
    {
        _settingsLoading = true;
        DarkSwitch.IsChecked = _settings.DarkTheme;
        LiquidSwitch.IsChecked = _settings.LiquidWaves;
        GameStatsSwitch.IsChecked = _settings.GameStats;
        KaraokeSwitch.IsChecked = _settings.Karaoke;
        NotifySwitch.IsChecked = _settings.MirrorNotifications;
        NotifyAllSwitch.IsChecked = _settings.MirrorAllApps;
        ColorBox.Text = _settings.IslandColor;
        HomeActionsChip.IsChecked = _settings.HomeActions;
        HomeMusicChip.IsChecked = _settings.HomeMusic;
        HomeWeatherChip.IsChecked = _settings.HomeWeather;
        foreach (CheckBox chip in ActionChips.Children)
            chip.IsChecked = _settings.Actions.Contains((string)chip.Tag);
        HoverSwitch.IsChecked = _settings.ExpandOnHover;
        FullscreenSwitch.IsChecked = _settings.HideInFullscreen;
        AutostartSwitch.IsChecked = Autostart.IsEnabled;
        CommandBox.Text = _settings.Command;
        TranslateBox.Text = _settings.TranslateTo;
        RecSysSwitch.IsChecked = _settings.RecordSystemAudio;
        RecMicSwitch.IsChecked = _settings.RecordMicrophone;
        RefreshClaudeHooksUi();
        _settingsLoading = false;
    }

    private void DarkSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsLoading) return;
        if ((DarkSwitch.IsChecked == true) != _settings.DarkTheme) ToggleTheme();
    }

    private void SetIslandColor(string hex)
    {
        if (Theme.Parse(hex) == null)
        {
            ColorBox.Text = _settings.IslandColor;
            return;
        }
        _settings.IslandColor = hex;
        _settings.Save();
        ColorBox.Text = hex;
        ApplyTheme();
        Kick(4);
    }

    private void Liquid_Click(object sender, RoutedEventArgs e)
    {
        _settings.LiquidWaves = LiquidSwitch.IsChecked == true;
        _settings.Save();
        if (_settings.LiquidWaves) Kick(5);
        else
        {
            _ripple = 0;
            _skew.Snap(0);
            _sx.Snap(1);
            _sy.Snap(1);
            UpdateShape(false);
        }
    }


    private void ColorApply_Click(object sender, RoutedEventArgs e) => SetIslandColor(NormalizeHex(ColorBox.Text));

    private void ColorBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SetIslandColor(NormalizeHex(ColorBox.Text));
    }

    private static string NormalizeHex(string s)
    {
        s = s.Trim();
        return s.StartsWith('#') ? s : "#" + s;
    }

    private void HomeLayout_Click(object sender, RoutedEventArgs e)
    {
        _settings.HomeActions = HomeActionsChip.IsChecked == true;
        _settings.HomeMusic = HomeMusicChip.IsChecked == true;
        _settings.HomeWeather = HomeWeatherChip.IsChecked == true;
        _settings.Save();
        BuildHome();
    }

    private void Behavior_Click(object sender, RoutedEventArgs e)
    {
        _settings.ExpandOnHover = HoverSwitch.IsChecked == true;
        _settings.HideInFullscreen = FullscreenSwitch.IsChecked == true;
        _settings.GameStats = GameStatsSwitch.IsChecked == true;
        _settings.Karaoke = KaraokeSwitch.IsChecked == true;
        _settings.MirrorNotifications = NotifySwitch.IsChecked == true;
        _settings.MirrorAllApps = NotifyAllSwitch.IsChecked == true;
        _settings.Save();
    }

    private void Autostart_Click(object sender, RoutedEventArgs e)
    {
        try { Autostart.Set(AutostartSwitch.IsChecked == true); }
        catch (Exception ex) { ShowToast("", "Автозапуск не изменён", ex.Message); }
        AutostartSwitch.IsChecked = Autostart.IsEnabled;
    }

    private void CommandBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _settings.Command = CommandBox.Text.Trim();
        _settings.Save();
    }

    private void TranslateBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var lang = TranslateBox.Text.Trim().ToLowerInvariant();
        _settings.TranslateTo = lang.Length is >= 2 and <= 5 ? lang : "ru";
        TranslateBox.Text = _settings.TranslateTo;
        _settings.Save();
    }

    private void RecordAudio_Click(object sender, RoutedEventArgs e)
    {
        _settings.RecordSystemAudio = RecSysSwitch.IsChecked == true;
        _settings.RecordMicrophone = RecMicSwitch.IsChecked == true;
        _settings.Save();
    }
}

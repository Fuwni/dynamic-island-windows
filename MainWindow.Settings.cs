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

    /// <summary>One row of the language list: its own name, and the English name as a hint.</summary>
    public sealed record LanguageOption(string Code, string Name, string Hint);

    private static readonly Dictionary<string, string> EnglishNames = new()
    {
        ["en"] = "", ["ru"] = "Russian", ["uk"] = "Ukrainian", ["es"] = "Spanish", ["de"] = "German", ["pt"] = "Portuguese", ["zh"] = "Chinese",
    };

    private void BuildLanguageList()
    {
        var auto = Services.Loc.Resolve("");
        var autoName = Services.Loc.Languages.First(l => l.Code == auto).Name;
        var options = new List<LanguageOption> { new("", L.T("Как в Windows"), autoName) };
        options.AddRange(Services.Loc.Languages.Select(l => new LanguageOption(l.Code, l.Name, EnglishNames[l.Code])));
        LanguageBox.ItemsSource = options;
        LanguageSub.Visibility = Services.Loc.Lang == "en" ? Visibility.Collapsed : Visibility.Visible;
        LanguageBox.SelectedItem = options.FirstOrDefault(o => o.Code == _settings.Language) ?? options[0];
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settingsLoading || LanguageBox.SelectedItem is not LanguageOption opt || opt.Code == _settings.Language) return;
        SwitchLanguage(opt.Code);
    }

    /// <summary>Save the language and, if it really changes, relaunch: the interface is translated at start-up.</summary>
    private void SwitchLanguage(string code)
    {
        _settings.Language = code;
        _settings.Save();
        if (Services.Loc.Resolve(code) == Services.Loc.Lang) return;
        try
        {
            ((App)Application.Current).ReleaseSingleInstance();   // let the new copy start right away
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, "--restart") { UseShellExecute = false });
            Close();
        }
        catch (Exception ex) { ShowToast("", L.T("Не получилось"), ex.Message); }
    }

    /// <summary>Quick uninstall from the settings: the installer's uninstaller, or a clean self-removal for a portable copy.</summary>
    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, L.T("Удалить Dynamic Island с этого компьютера?"), "Dynamic Island",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;

        var uninstaller = Services.Uninstaller.InstalledUninstaller;
        if (uninstaller != null)
        {
            // installed with the setup: its uninstaller removes everything and asks about personal data
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uninstaller) { UseShellExecute = true });
            Close();
            return;
        }

        bool data = MessageBox.Show(this, L.T("Удалить также настройки, заметки и вход в чаты?"), "Dynamic Island",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        Services.Uninstaller.RemoveRegistrations();
        Services.Uninstaller.ScheduleDelete(data, appFolder: true);
        Close();
    }

    private void InitSettingsPage()
    {
        BuildLanguageList();
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
        BuildCharacterGallery();
        BuildSettingsNav();
        BuildAuraChips();
        RefreshSettingsPage();
    }

    private void BuildCharacterGallery()
    {
        foreach (var (id, name) in Views.CharacterView.All)
        {
            var preview = new Views.CharacterView { Kind = id, LayoutTransform = new ScaleTransform(1.5, 1.5), HorizontalAlignment = HorizontalAlignment.Center };
            _previewCharacters.Add(preview);
            var stack = new StackPanel();
            stack.Children.Add(new Border { Height = 40, Child = preview });
            stack.Children.Add(new TextBlock { Text = name, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) });

            var card = new Border
            {
                Width = 126, Padding = new Thickness(6, 8, 6, 8), Margin = new Thickness(0, 0, 8, 8),
                CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1.5), Cursor = Cursors.Hand, Child = stack, Tag = id,
            };
            card.SetResourceReference(Border.BackgroundProperty, "ChipBg");
            card.MouseLeftButtonUp += (_, _) =>
            {
                if (_justDragged) return;
                _settings.Character = id;
                _settings.Save();
                Character.Kind = id;
                preview.Cheer();
                SetupAura(); // Claude Code lights the island orange
                HighlightCharacter();
            };
            CharacterGallery.Children.Add(card);
        }
        HighlightCharacter();
    }

    private void HighlightCharacter()
    {
        foreach (Border card in CharacterGallery.Children)
        {
            if ((string)card.Tag == _settings.Character) card.SetResourceReference(Border.BorderBrushProperty, "Accent");
            else card.BorderBrush = Brushes.Transparent;
        }
    }

    private void Reactive_Click(object sender, RoutedEventArgs e)
    {
        _settings.Reactive = ReactiveSwitch.IsChecked == true;
        _settings.Save();
        UpdateShape(false);
    }

    private void HandDrawn_Click(object sender, RoutedEventArgs e)
    {
        _settings.HandDrawn = HandDrawnSwitch.IsChecked == true;
        _settings.Save();
    }

    private void ClaudeGlow_Click(object sender, RoutedEventArgs e)
    {
        _settings.ClaudeGlow = ClaudeGlowSwitch.IsChecked == true;
        _settings.Save();
        _auraThinking = false;
        SetupAura();
    }

    private void RefreshSettingsPage()
    {
        _settingsLoading = true;
        DarkSwitch.IsChecked = _settings.DarkTheme;
        LiquidSwitch.IsChecked = _settings.LiquidWaves;
        RefreshLookControls();
        ReactiveSwitch.IsChecked = _settings.Reactive;
        ClaudeGlowSwitch.IsChecked = _settings.ClaudeGlow;
        HandDrawnSwitch.IsChecked = _settings.HandDrawn;
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
        catch (Exception ex) { ShowToast("", L.T("Автозапуск не изменён"), ex.Message); }
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
        _settings.TranslateTo = lang.Length is >= 2 and <= 5 ? lang : "";
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

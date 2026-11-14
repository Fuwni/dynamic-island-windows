using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// Automatic updates: a quiet check shortly after start and every 6 hours. A newer release shows a
/// notification on the island; one click downloads the verified installer and updates in place.
/// </summary>
public partial class MainWindow
{
    private Updater.UpdateInfo? _update;
    private bool _updating;
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(6) };

    private static bool IsToolRun => Environment.GetCommandLineArgs()
        .Any(a => a is "--snapshot" or "--promo" or "--review" or "--brand" or "--live-demo" or "--cover");

    private void InitUpdates()
    {
        AutoUpdateSwitch.IsChecked = _settings.AutoUpdateCheck;
        if (AppPackage.IsPackaged) { UpdatesTile.Visibility = Visibility.Collapsed; return; }   // the Store updates it
        RefreshUpdateUi();
        if (IsToolRun) return;   // no network checks while filming or rendering
        _updateTimer.Tick += async (_, _) => await CheckForUpdatesAsync(manual: false);
        _updateTimer.Start();
        Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(25));   // let the island settle first
            await CheckForUpdatesAsync(manual: false);
        });
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (!manual && !_settings.AutoUpdateCheck) return;
        if (manual) UpdateStatus.Text = L.T("Проверяю…");
        try
        {
            var found = await Updater.CheckAsync();
            bool isNew = found != null && found.Tag != _update?.Tag;
            _update = found;
            if (found != null && (manual || isNew))
                ShowToast("", L.F("Доступна версия {0}", found.Version.ToString(3)),
                    Updater.CanSelfUpdate ? L.T("Нажмите, чтобы обновить") : L.T("Нажмите, чтобы скачать"),
                    StartUpdate, (Brush)FindResource("AccentBlue"), ms: 9000);
        }
        catch (Exception ex)
        {
            Log.Write("update check: " + ex.Message);
            if (manual) { UpdateStatus.Text = L.T("Нет связи с GitHub"); return; }
        }
        RefreshUpdateUi();
    }

    private void RefreshUpdateUi()
    {
        var current = Updater.Current.ToString(3);
        if (_update == null)
        {
            UpdateStatus.Text = L.F("Установлена последняя версия · {0}", current);
            UpdateButton.Visibility = Visibility.Collapsed;
            return;
        }
        UpdateStatus.Text = L.F("Доступна версия {0} (у вас {1})", _update.Version.ToString(3), current);
        UpdateButton.Content = Updater.CanSelfUpdate && _update.SetupUrl != null
            ? L.F("Обновить до {0}", _update.Version.ToString(3))
            : L.T("Скачать с GitHub");
        UpdateButton.Visibility = Visibility.Visible;
    }

    private async void StartUpdate()
    {
        if (_update == null || _updating) return;
        if (!Updater.CanSelfUpdate || _update.SetupUrl == null)
        {
            SafeShell.OpenUrl(_update.PageUrl);   // portable copy: get the new version from the release page
            return;
        }
        _updating = true;
        UpdateButton.IsEnabled = false;
        var progress = new Progress<double>(p => UpdateStatus.Text = L.F("Скачиваю обновление… {0:0}%", p * 100));
        try
        {
            ShowToast("", L.F("Скачиваю Atoll {0}…", _update.Version.ToString(3)), null);
            var setup = await Updater.DownloadAsync(_update, progress);
            UpdateStatus.Text = L.T("Устанавливаю…");
            Updater.Install(setup);   // the installer closes the island and starts the new version
        }
        catch (Exception ex)
        {
            Log.Write("update: " + ex.Message);
            UpdateStatus.Text = L.T("Не получилось: ") + ex.Message;
            UpdateButton.IsEnabled = true;
            _updating = false;
        }
    }

    private void UpdateNow_Click(object sender, RoutedEventArgs e) => StartUpdate();

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(manual: true);

    private void AutoUpdate_Click(object sender, RoutedEventArgs e)
    {
        _settings.AutoUpdateCheck = AutoUpdateSwitch.IsChecked == true;
        _settings.Save();
    }
}

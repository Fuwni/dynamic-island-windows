using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>
/// Music played by Atoll itself, in the Music tab: a service's own site inside the island (signed in by the user,
/// nothing injected), internet radio and local files. All of them report to Windows media controls, so the island
/// drives them like any other player. What has not played for five minutes is closed and its memory freed.
/// </summary>
public partial class MainWindow
{
    private readonly AtollPlayer _player = new();
    private WebView2CompositionControl? _musicWeb;
    private string _musicStartUrl = "";
    private MusicPlayers.Player? _musicSite;                        // the site that is open
    private MusicPlayers.Player _musicSource = MusicPlayers.All[0]; // the chip that is chosen
    private bool _musicFullView;                                    // the player with lyrics instead of the open site
    private string _musicSubfolder = "";
    private int _musicSearch;
    private DateTime _musicPlayedAt = DateTime.Now;
    private readonly System.Windows.Threading.DispatcherTimer _musicIdleTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private static readonly TimeSpan MusicIdle = TimeSpan.FromMinutes(5);

    private bool MusicSiteShown => _musicWeb != null && _musicSource == _musicSite && !_musicFullView;

    private void InitMusicPlayer()
    {
        _player.Failed += () => Dispatcher.BeginInvoke(MusicFailed);
        _media.OwnSeek = _player.Seek;
        _musicIdleTimer.Tick += (_, _) => ReleaseIdleMusic();
        _musicSource = MusicPlayers.ById(_settings.MusicSource);
        foreach (var p in MusicPlayers.All)
        {
            var chip = new RadioButton { Style = (Style)FindResource("Segment"), GroupName = "MusicSource", Tag = p.Id, Content = p.ToString() };
            chip.Click += (_, _) => PickMusicSource(p.Id);
            MusicChips.Children.Add(chip);
        }
        MusicWebHost.SizeChanged += (_, _) =>
            MusicWebHost.Clip = new RectangleGeometry(new Rect(0, 0, MusicWebHost.ActualWidth, MusicWebHost.ActualHeight), 17, 17);
        ShowMusicPart();
    }

    private void MusicFailed() => ShowToast("\uE783", L.T("Не удалось включить"), L.T("Попробуйте другую станцию или файл"));

    /// <summary>A chip: opens that source right away (a site that is already open just comes back).</summary>
    private void PickMusicSource(string id)
    {
        _musicSource = MusicPlayers.ById(id);
        _settings.MusicSource = id;
        _settings.Save();
        _musicFullView = false;
        _musicSubfolder = "";
        ClearMusicResults();
        if (_musicSource.IsSite)
        {
            if (_musicSite == _musicSource) ShowMusicPart();
            else _ = OpenMusicSiteAsync(_musicSource, _musicSource.Home);
        }
        else _ = SearchMusicAsync();
    }

    /// <summary>The site with a one-line player, or the full player with what was found under it.</summary>
    private void ShowMusicPart()
    {
        foreach (RadioButton chip in MusicChips.Children) chip.IsChecked = (string)chip.Tag == _musicSource.Id;
        bool site = MusicSiteShown;
        MusicFull.Visibility = site ? Visibility.Collapsed : Visibility.Visible;
        MusicBar.Visibility = MusicWebFrame.Visibility = site ? Visibility.Visible : Visibility.Collapsed;
        MusicPage.Width = site ? 760 : 720;
        RefreshMusicButtons();
        RemeasureMusic();
    }

    private void RefreshMusicButtons()
    {
        static Visibility On(bool on) => on ? Visibility.Visible : Visibility.Collapsed;
        bool open = _musicWeb != null && _musicSource == _musicSite;
        MusicBackButton.Visibility = On(MusicSiteShown);
        MusicViewButton.Visibility = On(open);
        MusicViewButton.Content = MusicSiteShown ? "\uE70D" : "\uE70E";
        MusicFolderButton.Visibility = On(_musicSource.Id == "files");
        MusicStopButton.Visibility = On(_musicWeb != null || _player.Open);
    }

    private void RemeasureMusic() { if (_mode == Mode.Expanded && TabMusic.IsChecked == true) Remeasure(); }

    private void ClearMusicResults()
    {
        _musicSearch++;   // a search still running belongs to the previous source
        MusicResults.Children.Clear();
        MusicResultsScroll.Visibility = Visibility.Collapsed;
    }

    private void MusicView_Click(object sender, RoutedEventArgs e) { _musicFullView = !_musicFullView; ShowMusicPart(); }

    private void MusicBack_Click(object sender, RoutedEventArgs e) { if (_musicWeb?.CoreWebView2 is { CanGoBack: true } c) c.GoBack(); }

    private void MusicQuery_TextChanged(object sender, TextChangedEventArgs e) =>
        MusicQueryHint.Visibility = MusicQuery.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void MusicQuery_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) _ = SearchMusicAsync(); }

    private async Task SearchMusicAsync()
    {
        var p = _musicSource;
        var query = MusicQuery.Text.Trim();
        _musicFullView = false;
        ClearMusicResults();
        if (p.IsSite) { await OpenMusicSiteAsync(p, MusicPlayers.AddressFor(p, query)); return; }

        ShowMusicPart();
        int search = _musicSearch;
        try
        {
            if (p.Id == "radio")
            {
                MusicNote(L.T("Ищу станции…"));
                var found = await RadioDirectory.SearchAsync(query);
                if (search != _musicSearch) return;
                MusicResults.Children.Clear();
                StationRows(found);
                if (found.Count == 0) MusicNote(L.T("Ничего не найдено"));
            }
            else
            {
                var folder = _musicSubfolder.Length > 0 ? _musicSubfolder : _settings.MusicFolder;
                if (folder.Length == 0) { MusicNote(L.T("Выберите папку с музыкой")); return; }
                var found = await Task.Run(() => MusicPlayers.FindFiles(folder, query));
                if (search != _musicSearch) return;
                for (int i = 0; i < found.Count; i++)
                {
                    int at = i;
                    MusicRow("\uE8D6", Path.GetFileNameWithoutExtension(found[i]), "", () => PlayOwn(() => _player.PlayFiles(found, at)));
                }
                if (found.Count == 0) MusicNote(L.T("Ничего не найдено"));
            }
        }
        catch (Exception ex)
        {
            Log.Write("music: search: " + ex.GetType().Name + ": " + ex.Message);
            if (search != _musicSearch) return;
            MusicResults.Children.Clear();
            MusicNote(L.T("Поиск не удался"));
        }
        RemeasureMusic();
    }

    /// <summary>A site's own playlists page, the liked stations, or the folders of the user's music.</summary>
    private void MusicPlaylists_Click(object sender, RoutedEventArgs e) => _ = ShowPlaylistsAsync();

    private async Task ShowPlaylistsAsync()
    {
        _musicFullView = false;
        ClearMusicResults();
        if (_musicSource.IsSite) { await OpenMusicSiteAsync(_musicSource, _musicSource.Playlists); return; }
        int search = _musicSearch;
        if (_musicSource.Id == "radio")
        {
            StationRows(_settings.RadioLiked);
            if (_settings.RadioLiked.Count == 0) MusicNote(L.T("Нажмите ♥ у станции, и она появится здесь"));
        }
        else
        {
            var root = _settings.MusicFolder;
            if (root.Length == 0) { MusicNote(L.T("Выберите папку с музыкой")); return; }
            List<string> folders = [];
            try { folders = await Task.Run(() => MusicPlayers.FindFolders(root)); }
            catch (Exception ex) { Log.Write("music: folders: " + ex.GetType().Name); }
            if (search != _musicSearch) return;
            foreach (var f in folders.Prepend(""))
                MusicRow("\uE8B7", f.Length == 0 ? L.T("Вся папка") : Path.GetRelativePath(root, f), "", () => { _musicSubfolder = f; MusicQuery.Text = ""; _ = SearchMusicAsync(); });
        }
        RemeasureMusic();
    }

    private void MusicNote(string text)
    {
        MusicResults.Children.Add(new TextBlock { Text = text, Style = (Style)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 4, 0, 4) });
        MusicResultsScroll.Visibility = Visibility.Visible;
        RemeasureMusic();
    }

    private void StationRows(IEnumerable<RadioDirectory.Station> stations)
    {
        foreach (var s in stations)
        {
            bool Liked() => _settings.RadioLiked.Any(x => x.Url == s.Url);
            var heart = new Button { Style = (Style)FindResource("IconButton"), Content = Liked() ? "\uEB52" : "\uEB51", FontSize = 12, Width = 28, Height = 28 };
            heart.Click += (_, _) =>
            {
                if (Liked()) _settings.RadioLiked.RemoveAll(x => x.Url == s.Url); else _settings.RadioLiked.Insert(0, s);
                _settings.Save();
                heart.Content = Liked() ? "\uEB52" : "\uEB51";
            };
            MusicRow("\uE93E", s.Name, s.Info, () => { PlayOwn(() => _player.PlayRadio(s)); _ = RadioDirectory.CountAsync(s); }, heart);
        }
    }

    private void MusicRow(string icon, string title, string info, Action play, Button? extra = null)
    {
        var line = new Grid { Width = 630 };   // the pill centres its content
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        line.ColumnDefinitions.Add(new ColumnDefinition());
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var sign = new TextBlock { Text = icon, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = title, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var right = new TextBlock { Text = info, FontSize = 11, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        sign.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        right.SetResourceReference(TextBlock.ForegroundProperty, "Fg3");
        Grid.SetColumn(name, 1);
        Grid.SetColumn(right, 2);
        line.Children.Add(sign);
        line.Children.Add(name);
        line.Children.Add(right);
        var b = new Button { Style = (Style)FindResource("GhostPill"), Padding = new Thickness(8, 6, 8, 6), Content = line };
        b.Click += (_, _) => play();
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        if (extra != null) { DockPanel.SetDock(extra, Dock.Right); row.Children.Add(extra); }
        row.Children.Add(b);
        MusicResults.Children.Add(row);
        MusicResultsScroll.Visibility = Visibility.Visible;
    }

    /// <summary>Radio or a file; a site that plays is paused first (one sound at a time).</summary>
    private void PlayOwn(Action play)
    {
        if (_musicWeb?.CoreWebView2?.IsDocumentPlayingAudio == true) _ = _media.Pause("msedgewebview2.exe");
        try { play(); }
        catch (Exception ex)
        {
            Log.Write("music: play: " + ex.GetType().Name + ": " + ex.Message);
            MusicFailed();
        }
        _musicPlayedAt = DateTime.Now;
        _musicIdleTimer.Start();
        RefreshMusicButtons();
    }

    private void MusicFolder_Click(object sender, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFolderDialog { Title = L.T("Выберите папку с музыкой") };
        if (d.ShowDialog() != true) return;
        _settings.MusicFolder = d.FolderName;
        _settings.Save();
        _musicSubfolder = "";
        _ = SearchMusicAsync();
    }

    private void MusicStop_Click(object sender, RoutedEventArgs e)
    {
        _player.Close();
        CloseMusicSite();
    }

    // ------------------------------------------------------------------ the services' own sites

    private async Task OpenMusicSiteAsync(MusicPlayers.Player site, string url)
    {
        _musicSite = site;
        MediaService.WebPlayerName = site.Name;
        _musicPlayedAt = DateTime.Now;
        _musicStartUrl = url;
        if (_musicWeb != null)
        {
            _musicWeb.CoreWebView2?.Navigate(url);   // null: still starting, it goes to _musicStartUrl
            ShowMusicPart();
            return;
        }

        // the composition control draws inside the island's transparent window, like the chat
        var web = new WebView2CompositionControl { Visibility = Visibility.Hidden };
        _musicWeb = web;
        MusicWebNote.Visibility = Visibility.Visible;
        MusicWebHost.Children.Add(web);
        ShowMusicPart();
        _musicIdleTimer.Start();
        try
        {
            // test runs have a profile of their own: the user's sign-ins stay out of them
            var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", App.TestRun ? "WebView2MusicTest" : "WebView2Music");
            var env = await CoreWebView2Environment.CreateAsync(null, dataDir);
            if (_musicWeb != web) return;   // closed meanwhile
            await web.EnsureCoreWebView2Async(env);
            if (_musicWeb != web) return;
            web.ZoomFactor = 0.8;   // more of the page in the island
            var core = web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.PermissionRequested += (_, e) =>
            {
                if (e.PermissionKind is CoreWebView2PermissionKind.Microphone or CoreWebView2PermissionKind.Camera
                    or CoreWebView2PermissionKind.Geolocation or CoreWebView2PermissionKind.Notifications
                    or CoreWebView2PermissionKind.ClipboardRead) e.State = CoreWebView2PermissionState.Deny;
            };
            core.NavigationStarting += (_, e) =>
            {
                var u = e.Uri ?? "";
                if (!u.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !u.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
            };
            core.LaunchingExternalUriScheme += (_, e) => e.Cancel = true;   // "open in the app": no, it plays here
            core.NewWindowRequested += (_, e) =>
            {
                var u = e.Uri ?? "";
                if (SafeShell.IsOnHost(u, MusicPlayers.Hosts) || u.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return;   // a sign-in pop-up
                e.Handled = true;
                SafeShell.OpenUrl(u);
            };
            core.IsDocumentPlayingAudioChanged += (_, _) => { if (core.IsDocumentPlayingAudio) _player.Pause(); };
            core.NavigationCompleted += (_, _) => { MusicWebNote.Visibility = Visibility.Collapsed; web.Visibility = Visibility.Visible; };
            core.ProcessFailed += (_, e) =>
            {
                Log.Write("music: web: " + e.ProcessFailedKind);
                if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited && _musicWeb == web) Dispatcher.BeginInvoke(CloseMusicSite);
            };
            // the site's shim and styles, strictly before the first navigation
            await MusicWebExtras.AttachAsync(core, site.Id);
            if (_musicWeb != web) return;
            core.Navigate(_musicStartUrl);
        }
        catch (Exception ex)
        {
            Log.Write("music: web: " + ex.GetType().Name + ": " + ex.Message);
            if (_musicWeb != web) return;
            CloseMusicSite();
            ShowToast("", L.T("Не удалось включить"), site.Name);
        }
    }

    /// <summary>Closes the site and its browser processes; the sign-in stays (cookies are on disk).</summary>
    private void CloseMusicSite()
    {
        var web = _musicWeb;
        _musicWeb = null;
        _musicSite = null;
        MediaService.WebPlayerName = "";
        try
        {
            if (web != null) { MusicWebHost.Children.Remove(web); web.Dispose(); }
        }
        catch (Exception ex) { Log.Write("music: dispose: " + ex.GetType().Name + ": " + ex.Message); }
        if (!_shutDown) ShowMusicPart();
    }

    /// <summary>Nothing has played for five minutes: free the memory. A site the user is looking at stays.</summary>
    private void ReleaseIdleMusic()
    {
        if (_player.Playing || _musicWeb?.CoreWebView2?.IsDocumentPlayingAudio == true || (_mode == Mode.Expanded && MusicWebHost.IsVisible))
        {
            _musicPlayedAt = DateTime.Now;
            return;
        }
        if (DateTime.Now - _musicPlayedAt < MusicIdle) return;
        _musicIdleTimer.Stop();
        _player.Close();
        CloseMusicSite();
    }

    private void ShutdownMusicPlayer()
    {
        _musicIdleTimer.Stop();
        _player.Close();
        Process? browser = null;
        try { if (_musicWeb?.CoreWebView2 is { } core) browser = Process.GetProcessById((int)core.BrowserProcessId); }
        catch (Exception ex) { Log.Write("music: shutdown: " + ex.GetType().Name); }
        CloseMusicSite();
        using (browser) browser?.WaitForExit(2500);   // let it write its cookies
    }
}

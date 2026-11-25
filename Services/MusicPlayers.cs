using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using global::Windows.Media.Core;
using global::Windows.Media.Playback;
using global::Windows.Storage;
using global::Windows.Storage.FileProperties;
using global::Windows.Storage.Streams;

namespace DynamicIsland.Services;

/// <summary>What Atoll itself can play: the services' own sites, internet radio and the user's files.</summary>
public static class MusicPlayers
{
    /// <summary>A source in the Music tab; a site has an address and a search address ({0} = the escaped query) and the page of the user's playlists.</summary>
    public sealed record Player(string Id, string Name, string Home = "", string Search = "", string Playlists = "")
    {
        public bool IsSite => Home.Length > 0;
        public override string ToString() => L.T(Name);
    }

    public static readonly Player[] All =
    [
        new("ytmusic", "YouTube Music", "https://music.youtube.com/", "https://music.youtube.com/search?q={0}", "https://music.youtube.com/library/playlists"),
        new("youtube", "YouTube", "https://www.youtube.com/", "https://www.youtube.com/results?search_query={0}", "https://www.youtube.com/feed/playlists"),
        new("spotify", "Spotify", "https://open.spotify.com/", "https://open.spotify.com/search/{0}", "https://open.spotify.com/collection/playlists"),
        new("soundcloud", "SoundCloud", "https://soundcloud.com/", "https://soundcloud.com/search?q={0}", "https://soundcloud.com/you/sets"),
        new("deezer", "Deezer", "https://www.deezer.com/", "https://www.deezer.com/search/{0}", "https://www.deezer.com/profile/me/playlists"),
        new("apple", "Apple Music", "https://music.apple.com/", "https://music.apple.com/search?term={0}", "https://music.apple.com/library/all-playlists"),
        // VK doesn't report to Windows media controls: its shim and the ad cleanup live in Assets/music-extras/vk.js and vk.css
        new("vk", "VK Музыка", "https://vk.com/audio", "https://vk.com/audio?q={0}", "https://vk.com/audio"),
        new("radio", "Радио"),
        new("files", "Свои файлы"),
    ];

    /// <summary>Pop-ups that stay in Atoll: the services and their sign-in pages.</summary>
    public static readonly string[] Hosts =
    [
        "youtube.com", "spotify.com", "soundcloud.com", "deezer.com", "music.apple.com",
        "accounts.google.com", "appleid.apple.com", "idmsa.apple.com", "www.facebook.com",
        "vk.com", "vk.ru",   // signing in goes through id.vk.com, which is under .vk.com
    ];

    /// <summary>The source with this id; the first one when it is unknown.</summary>
    public static Player ById(string id) => All.FirstOrDefault(p => p.Id == id) ?? All[0];

    /// <summary>The folders inside the music folder that hold music: its playlists.</summary>
    public static List<string> FindFolders(string folder) =>
        FindFiles(folder, "", 5000).Select(f => Path.GetDirectoryName(f)!).Distinct().Where(d => d != folder.TrimEnd('\\', '/')).ToList();

    public static string AddressFor(Player site, string query) =>
        string.IsNullOrWhiteSpace(query) ? site.Home : string.Format(site.Search, Uri.EscapeDataString(query.Trim()));

    private static readonly string[] Types = [".mp3", ".m4a", ".aac", ".flac", ".wav", ".wma", ".ogg", ".opus"];

    /// <summary>The music files of a folder (and its subfolders) whose path has every word of the query.</summary>
    public static List<string> FindFiles(string folder, string query, int max = 200)
    {
        if (!Directory.Exists(folder)) return [];
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
            .Where(f => Types.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Where(f => words.All(w => Path.GetRelativePath(folder, f).Contains(w, StringComparison.CurrentCultureIgnoreCase)))
            .Take(max).OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}

/// <summary>Radio Browser: a free directory of stations (radio-browser.info), no key.</summary>
public static class RadioDirectory
{
    public sealed record Station(string Id, string Name, string Url, string Info);

    private static readonly HttpClient Http = CreateClient();
    private static string _host = "";

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        var version = typeof(RadioDirectory).Assembly.GetName().Version?.ToString(3) ?? "1";
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Atoll/{version}");
        return http;
    }

    /// <summary>One of the directory's servers, picked through DNS as its docs ask.</summary>
    private static async Task<string> HostAsync()
    {
        if (_host.Length > 0) return _host;
        try
        {
            var all = await Dns.GetHostAddressesAsync("all.api.radio-browser.info");
            var name = (await Dns.GetHostEntryAsync(all[Random.Shared.Next(all.Length)])).HostName;
            if (name.EndsWith(".api.radio-browser.info", StringComparison.OrdinalIgnoreCase)) return _host = name;
        }
        catch (Exception ex) { Log.Write("radio: servers: " + ex.GetType().Name); }
        return _host = "de1.api.radio-browser.info";
    }

    /// <summary>An empty query gives the most listened stations.</summary>
    public static async Task<List<Station>> SearchAsync(string query)
    {
        var name = query.Trim().Length > 0 ? "&name=" + Uri.EscapeDataString(query.Trim()) : "";
        try { return Parse(await Http.GetStringAsync($"https://{await HostAsync()}/json/stations/search?limit=60&hidebroken=true&is_https=true&order=clickcount&reverse=true{name}")); }
        catch { _host = ""; throw; }   // the next search picks another server
    }

    public static List<Station> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<Station>();
        foreach (var s in doc.RootElement.EnumerateArray())
        {
            string Text(string key) => s.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";
            var url = Text("url_resolved");
            var name = Text("name");
            if (name.Length == 0 || !SafeShell.IsWebUrl(url) || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) continue;
            // SomaFM's terms don't allow other programs to play its streams
            if ((name + url + Text("homepage")).Contains("somafm", StringComparison.OrdinalIgnoreCase) || name.Contains("soma fm", StringComparison.OrdinalIgnoreCase)) continue;
            if (list.Any(x => x.Url == url)) continue;
            int rate = s.TryGetProperty("bitrate", out var b) && b.ValueKind == JsonValueKind.Number && b.TryGetInt32(out var n) ? n : 0;
            list.Add(new(Text("stationuuid"), name, url, string.Join(" · ", new[] { Text("countrycode"), Text("codec"), rate > 0 ? rate + " kbps" : "" }.Where(x => x.Length > 0))));
        }
        return list;
    }

    /// <summary>The directory counts listens; its docs ask players to report each start.</summary>
    public static async Task CountAsync(Station s)
    {
        if (!Guid.TryParse(s.Id, out var id)) return;
        try { await Http.GetStringAsync($"https://{await HostAsync()}/json/url/{id}"); }
        catch (Exception ex) { Log.Write("radio: count: " + ex.GetType().Name); }
    }
}

/// <summary>
/// Windows' own player for radio and files. It reports to Windows media controls like any other player, so the
/// island's buttons, cover and lyrics work through <see cref="MediaService"/> with nothing special.
/// </summary>
public sealed class AtollPlayer
{
    private MediaPlayer? _player;
    private MediaPlaybackList? _list;

    public event Action? Failed;

    /// <summary>A radio station is on (its name is not a song: no lyrics).</summary>
    public bool Radio { get; private set; }
    public bool Open => _player != null;
    public bool Playing => _player?.PlaybackSession.PlaybackState is MediaPlaybackState.Playing or MediaPlaybackState.Buffering or MediaPlaybackState.Opening;

    public void PlayRadio(RadioDirectory.Station station)
    {
        Close();
        Radio = true;
        Start(Item(new Uri(station.Url), station.Name));
    }

    /// <summary>Plays this file and goes on through the list.</summary>
    public void PlayFiles(IReadOnlyList<string> files, int index)
    {
        Close();
        var list = new MediaPlaybackList();
        foreach (var f in files) list.Items.Add(Item(new Uri(f), Path.GetFileNameWithoutExtension(f)));
        list.StartingItem = list.Items[index];
        list.CurrentItemChanged += (sender, e) => { if (e.NewItem is { } item && sender.Items.IndexOf(item) is >= 0 and var i) _ = TagAsync(item, files[i]); };
        list.ItemFailed += (_, e) => Fail(e.Error.ErrorCode.ToString());
        _list = list;
        Start(list);
    }

    /// <summary>False when there is nothing to move in (radio).</summary>
    public bool Seek(TimeSpan to)
    {
        if (Radio || _player?.PlaybackSession is not { CanSeek: true } s) return false;
        s.Position = to;
        return true;
    }

    public TimeSpan Position => _player?.PlaybackSession.Position ?? TimeSpan.Zero;

    public void Pause() => _player?.Pause();

    /// <summary>Stops and frees the player.</summary>
    public void Close()
    {
        var p = _player;
        var items = _list?.Items.ToList() ?? (p?.Source is MediaPlaybackItem one ? [one] : []);
        _player = null; _list = null;
        Radio = false;
        if (p == null) return;
        p.Source = null;
        p.Dispose();
        foreach (var i in items) i.Source.Dispose();
    }

    private static MediaPlaybackItem Item(Uri uri, string title)
    {
        var item = new MediaPlaybackItem(MediaSource.CreateFromUri(uri));
        var d = item.GetDisplayProperties();
        d.Type = global::Windows.Media.MediaPlaybackType.Music;
        d.MusicProperties.Title = title;
        item.ApplyDisplayProperties(d);
        return item;
    }

    private void Start(IMediaPlaybackSource source)
    {
        var p = new MediaPlayer { AutoPlay = true, AudioCategory = MediaPlayerAudioCategory.Media };
        p.MediaFailed += (_, e) => Fail(e.Error.ToString());
        _player = p;
        p.Source = source;
    }

    private void Fail(string why)
    {
        Log.Write("player: " + why);
        Failed?.Invoke();
    }

    /// <summary>Title, artist, album and cover from the file's own tags.</summary>
    private static async Task TagAsync(MediaPlaybackItem item, string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            var tags = await file.Properties.GetMusicPropertiesAsync();
            var d = item.GetDisplayProperties();
            if (tags.Title.Length > 0) d.MusicProperties.Title = tags.Title;
            d.MusicProperties.Artist = tags.Artist;
            d.MusicProperties.AlbumTitle = tags.Album;
            var cover = await file.GetThumbnailAsync(ThumbnailMode.MusicView, 400);
            if (cover is { Type: ThumbnailType.Image }) d.Thumbnail = RandomAccessStreamReference.CreateFromStream(cover);
            item.ApplyDisplayProperties(d);
        }
        catch (Exception ex) { Log.Write("player: tags: " + ex.GetType().Name); }
    }
}

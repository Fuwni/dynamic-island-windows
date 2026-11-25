using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DynamicIsland.Services;

public sealed record LyricLine(TimeSpan Time, string Text);

/// <param name="Synced">true = each line has a timestamp; false = plain text only.</param>
/// <param name="Instrumental">The track has no words (LRCLIB says so).</param>
public sealed record Lyrics(IReadOnlyList<LyricLine> Lines, bool Synced, bool Instrumental = false);

/// <summary>A search result, reduced to what choosing one needs.</summary>
public sealed record LyricsCandidate(double Duration, bool Synced, bool Plain, bool Instrumental, string Artist = "");

/// <summary>
/// Lyrics from LRCLIB (lrclib.net): free, no API key, time-synced LRC. Asked once per track and kept on disk
/// (%APPDATA%\DynamicIsland\lyrics), so a song played again needs no network; "not found" is kept for 3 days.
/// </summary>
public sealed partial class LyricsService
{
    private static readonly HttpClient Http = CreateClient();
    private readonly Dictionary<string, Lyrics?> _cache = new();
    private DateTime _pauseUntil;

    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "lyrics");

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        // LRCLIB asks clients to say who they are (an identified client also gets a higher rate limit)
        var version = typeof(LyricsService).Assembly.GetName().Version?.ToString(3) ?? "1";
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Atoll/{version} (+https://github.com/Fuwni/dynamic-island-windows)");
        return http;
    }

    public async Task<Lyrics?> GetAsync(string artist, string title, TimeSpan duration, string album = "")
    {
        (artist, title) = Clean(artist, title);
        if (title.Length == 0) return null;

        var cacheKey = $"v2|{artist}\n{title}\n{Math.Round(duration.TotalSeconds / 2)}".ToLowerInvariant();

        // a time-synced result is final, but plain text of an older lookup is worth another try:
        // it may now be found synced by a source added later (only not-found entries expire on disk)
        Lyrics? cachedLyrics = null;
        if (_cache.TryGetValue(cacheKey, out var inMemory)) cachedLyrics = inMemory;
        else if (FromDisk(cacheKey) is { } disk) { cachedLyrics = disk.Lyrics; _cache[cacheKey] = cachedLyrics; }
        if (cachedLyrics is { Synced: true }) return cachedLyrics;
        if (DateTime.Now < _pauseUntil) return cachedLyrics;   // asked to slow down: not now, not cached

        try
        {
            Lyrics? result = cachedLyrics;
            double seconds = Math.Round(duration.TotalSeconds);

            // 1) exact match (artist + title + duration within ±2 s on LRCLIB's side; album helps)
            if (result == null && artist.Length > 0 && duration > TimeSpan.Zero)
            {
                var url = $"https://lrclib.net/api/get?track_name={E(title)}&artist_name={E(artist)}&duration={seconds}" +
                          (album.Length > 0 ? $"&album_name={E(album)}" : "");
                using var doc = await TryGetJsonAsync(url);
                if (doc != null) result = FromRecord(doc.RootElement);
            }

            // 2) search: by artist and title, then as free text; only results within ±2 s of this track
            if (result == null)
            {
                var mainArtist = MainArtist(artist);
                foreach (var url in new[]
                {
                    mainArtist.Length > 0 ? $"https://lrclib.net/api/search?track_name={E(title)}&artist_name={E(mainArtist)}" : null,
                    $"https://lrclib.net/api/search?q={E((mainArtist + " " + title).Trim())}",
                    // by the title alone: players like VK spell the artist differently from LRCLIB ("Артист feat. X",
                    // Latin vs Cyrillic); taken only with the same length and an artist that still matches
                    mainArtist.Length > 0 && duration > TimeSpan.Zero ? $"https://lrclib.net/api/search?track_name={E(title)}" : null,
                })
                {
                    if (url == null || result != null) continue;
                    using var doc = await TryGetJsonAsync(url);
                    if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Array) continue;
                    var items = doc.RootElement.EnumerateArray().ToList();
                    bool titleOnly = !url.Contains("artist_name=") && !url.Contains("?q=");
                    int best = PickBest(items.Select(ToCandidate).ToList(), duration.TotalSeconds, titleOnly ? mainArtist : null);
                    if (best >= 0) result = FromRecord(items[best]);
                }
            }

            // 3) NetEase: ready-made time-synced LRC, including Russian tracks LRCLIB doesn't have.
            // Asked also when LRCLIB had plain text only: a karaoke line beats a wall of words.
            if (result == null || !result.Synced)
            {
                var synced = await FromNetEaseAsync(artist, title, duration);
                if (synced != null) result = synced;
            }

            _cache[cacheKey] = result;
            ToDisk(cacheKey, result);
            Log.Write($"lyrics: {artist} — {title} (dur {(int)duration.TotalSeconds}): " +
                      (result == null ? "not found" : result.Synced ? "found, time-synced" : "found, plain text"));
            return result;
        }
        catch (Exception e)
        {
            Log.Write("lyrics: " + e.Message);
            return null; // network problem: don't cache, try again next time
        }
    }

    /// <summary>
    /// NetEase Cloud Music: its open search has many ready-made LRC files with timestamps (including Russian
    /// tracks missing from LRCLIB). Only a title+artist match within ±5 s of the track's length is taken.
    /// </summary>
    private async Task<Lyrics?> FromNetEaseAsync(string artist, string title, TimeSpan duration)
    {
        try
        {
            var query = (MainArtist(artist) + " " + title).Trim();
            if (title.Length == 0) return null;

            using var search = await TryGetJsonAsync($"https://music.163.com/api/search/get?s={E(query)}&type=1&limit=10");
            if (search == null) return null;
            if (!search.RootElement.TryGetProperty("result", out var found) || found.ValueKind != JsonValueKind.Object) return null;
            if (!found.TryGetProperty("songs", out var songs) || songs.ValueKind != JsonValueKind.Array) return null;

            var found2 = new List<NetEaseSong>();
            foreach (var s in songs.EnumerateArray())
            {
                if (!s.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number) continue;
                long songId;
                try { songId = id.GetInt64(); } catch { continue; }   // one odd id must not drop every result
                var name = s.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var who = "";
                if (s.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array && artists.GetArrayLength() > 0)
                    who = artists[0].TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";
                var ms = s.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0;
                found2.Add(new NetEaseSong(songId, name, who, ms / 1000.0));
            }

            var bestId = PickNetEase(found2, artist, title, duration);
            if (bestId == 0) return null;

            using var doc = await TryGetJsonAsync($"https://music.163.com/api/song/lyric?id={bestId}&lv=1&kv=1&tv=1");
            if (doc == null || !doc.RootElement.TryGetProperty("lrc", out var lrc) || lrc.ValueKind != JsonValueKind.Object) return null;
            var text = lrc.TryGetProperty("lyric", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";
            if (text.Length == 0) return null;

            var lines = ParseLrc(CreditsLine().Replace(text, ""))
                .Where(l => l.Text.Trim(' ', '.', '-', '…', '　').Length > 0)
                .ToList();
            if (lines.Count == 0) return null;
            Log.Write($"lyrics: netease: {artist} — {title}: {lines.Count} time-synced lines");
            return new Lyrics(lines, true);
        }
        catch (Exception e) { Log.Write("lyrics: netease: " + e.Message); return null; }
    }

    /// <summary>A NetEase search hit: its id, title, artist and length in seconds.</summary>
    internal readonly record struct NetEaseSong(long Id, string Name, string Artist, double Seconds);

    /// <summary>Both names are the same title once case, ё/е, punctuation and spacing are ignored.</summary>
    internal static bool SameTitle(string a, string b)
    {
        static string N(string s) => new(s.ToLowerInvariant().Replace('ё', 'е').Where(char.IsLetterOrDigit).ToArray());
        var x = N(a);
        return x.Length > 0 && x == N(b);
    }

    /// <summary>
    /// Which NetEase hit belongs to this track: the same artist, the same title and, when the length is
    /// known, a length within 5 s. 0 — none fits.
    /// </summary>
    internal static long PickNetEase(IEnumerable<NetEaseSong> songs, string artist, string title, TimeSpan duration)
    {
        long bestId = 0;
        int bestScore = 0;
        foreach (var s in songs)
        {
            if (duration > TimeSpan.Zero && s.Seconds > 0 && Math.Abs(s.Seconds - duration.TotalSeconds) > 5) continue;
            if (!SameArtist(s.Artist, artist)) continue;
            // titles are compared exactly (normalised): a longer «Sayonara (Live)» is another recording
            if (!SameTitle(s.Name, title)) continue;
            int score = 2 + 1;
            if (score <= bestScore) continue;
            bestScore = score;
            bestId = s.Id;
        }
        return bestId;
    }

    /// <summary>NetEase LRC credits lines: «作词 : …», «作曲 : …» and the other titles.</summary>
    [GeneratedRegex(@"^\s*\[[^\]]*\]\s*(作词|作曲|编曲|制作|混音|母带|录音|和声|吉他|贝斯|鼓|键盘|出品|监制|统筹|发行|词|曲|OP|SP)\s*[:：].*$", RegexOptions.Multiline)]
    private static partial Regex CreditsLine();

    private async Task<JsonDocument?> TryGetJsonAsync(string url)
    {
        using var response = await Http.GetAsync(url);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(60);
            _pauseUntil = DateTime.Now + wait + TimeSpan.FromSeconds(Random.Shared.NextDouble() * wait.TotalSeconds * 0.3);
            throw new HttpRequestException($"LRCLIB asks to wait {wait.TotalSeconds:0} s");
        }
        if (!response.IsSuccessStatusCode)
        {
            Log.Write($"lyrics: HTTP {(int)response.StatusCode} {url}");
            return null;
        }
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static LyricsCandidate ToCandidate(JsonElement e) => new(
        e.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0,
        HasText(e, "syncedLyrics"), HasText(e, "plainLyrics"),
        e.TryGetProperty("instrumental", out var i) && i.ValueKind == JsonValueKind.True,
        e.TryGetProperty("artistName", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString()! : "");

    /// <summary>
    /// The best search result for a track of this length: only those within ±2 s (when the length is known),
    /// synced before plain, words before "instrumental". −1 when none fits.
    /// </summary>
    public static int PickBest(IReadOnlyList<LyricsCandidate> items, double duration, string? artist = null)
    {
        int best = -1, bestRank = 0;
        for (int i = 0; i < items.Count; i++)
        {
            var c = items[i];
            if (duration > 0 && c.Duration > 0 && Math.Abs(c.Duration - duration) > 2) continue;
            if (artist != null && !SameArtist(c.Artist, artist)) continue;
            int rank = c.Synced ? 3 : c.Plain ? 2 : c.Instrumental ? 1 : 0;
            if (rank > bestRank) { best = i; bestRank = rank; }
        }
        return best;
    }

    private static bool HasText(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String && v.GetString()!.Length > 0;

    private static Lyrics? FromRecord(JsonElement e)
    {
        if (HasText(e, "syncedLyrics"))
        {
            var lines = ParseLrc(e.GetProperty("syncedLyrics").GetString()!);
            if (lines.Count > 0) return new Lyrics(lines, true);
        }
        if (HasText(e, "plainLyrics"))
        {
            var lines = e.GetProperty("plainLyrics").GetString()!
                .Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .Select(l => new LyricLine(TimeSpan.Zero, l))
                .ToList();
            if (lines.Count > 0) return new Lyrics(lines, false);
        }
        if (e.TryGetProperty("instrumental", out var i) && i.ValueKind == JsonValueKind.True) return new Lyrics([], false, Instrumental: true);
        return null;
    }

    /// <summary>
    /// LRC to lines: "[mm:ss.xx]text" (also [mm:ss:xx] and [mm:ss]), several stamps on one line, tags like
    /// [ar:…] skipped; [offset:+250] moves every line earlier by 250 ms (negative: later).
    /// </summary>
    public static List<LyricLine> ParseLrc(string lrc)
    {
        var result = new List<LyricLine>();
        double offsetMs = 0;
        foreach (var raw in lrc.Split('\n'))
        {
            var off = OffsetTag().Match(raw);
            if (off.Success) { offsetMs = double.Parse(off.Groups[1].Value, CultureInfo.InvariantCulture); continue; }
            var stamps = TimeTag().Matches(raw);
            if (stamps.Count == 0) continue;
            var text = TimeTag().Replace(raw, "").Trim();
            foreach (Match m in stamps)
            {
                var minutes = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                var seconds = double.Parse(m.Groups[2].Value.Replace(':', '.'), CultureInfo.InvariantCulture);
                result.Add(new LyricLine(TimeSpan.FromSeconds(minutes * 60 + seconds), text));
            }
        }
        if (offsetMs != 0)
            for (int i = 0; i < result.Count; i++)
                result[i] = result[i] with { Time = TimeSpan.FromMilliseconds(Math.Max(0, result[i].Time.TotalMilliseconds - offsetMs)) };
        result.Sort((a, b) => a.Time.CompareTo(b.Time));
        return result;
    }

    /// <summary>The line being sung at t (the last one that started at or before t), −1 before the first.</summary>
    public static int LineAt(IReadOnlyList<LyricLine> lines, TimeSpan t)
    {
        int lo = 0, hi = lines.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (lines[mid].Time <= t) { found = mid; lo = mid + 1; } else hi = mid - 1;
        }
        return found;
    }

    // ------------------------------------------------------------------ disk cache

    private sealed record Stored(bool Found, bool Synced, bool Instrumental, List<double> Times, List<string> Texts, DateTime At);

    private static string FileFor(string key) =>
        Path.Combine(Folder, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)))[..20] + ".json");

    private static (Lyrics? Lyrics, bool Hit)? FromDisk(string key)
    {
        try
        {
            var path = FileFor(key);
            if (!File.Exists(path)) return null;
            var s = JsonSerializer.Deserialize<Stored>(File.ReadAllText(path));
            if (s == null) return null;
            if (!s.Found) return DateTime.Now - s.At < TimeSpan.FromDays(3) ? (null, true) : null;   // ask again after a few days
            var lines = s.Times.Zip(s.Texts, (t, x) => new LyricLine(TimeSpan.FromMilliseconds(t), x)).ToList();
            return (new Lyrics(lines, s.Synced, s.Instrumental), true);
        }
        catch (Exception e) { Log.Write("lyrics cache read: " + e.Message); return null; }
    }

    private static void ToDisk(string key, Lyrics? l)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var s = new Stored(l != null, l?.Synced ?? false, l?.Instrumental ?? false,
                               l?.Lines.Select(x => x.Time.TotalMilliseconds).ToList() ?? [], l?.Lines.Select(x => x.Text).ToList() ?? [], DateTime.Now);
            var path = FileFor(key);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(s));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (Exception e) { Log.Write("lyrics cache write: " + e.Message); }
    }

    /// <summary>
    /// One artist is the other, ignoring case, ё/е, spaces and punctuation ("Би-2" = "би 2"), and Cyrillic against
    /// Latin ("Анна Асти" = "ANNA ASTI"): Yandex and VK often spell an artist one way and LRCLIB the other.
    /// </summary>
    public static bool SameArtist(string a, string b)
    {
        static string N(string s) => new(s.ToLowerInvariant().Replace('ё', 'е').Where(char.IsLetterOrDigit).ToArray());
        static bool In(string x, string y) => x.Length > 0 && y.Length > 0 && (x.Contains(y) || y.Contains(x));
        string x = N(a), y = N(b);
        return In(x, y) || In(Latin(x), Latin(y));
    }

    private static string Latin(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (var c in s)
            sb.Append(c switch
            {
                'а' => "a", 'б' => "b", 'в' => "v", 'г' => "g", 'ґ' => "g", 'д' => "d", 'е' => "e", 'є' => "ye", 'ж' => "zh",
                'з' => "z", 'и' => "i", 'і' => "i", 'ї' => "yi", 'й' => "y", 'к' => "k", 'л' => "l", 'м' => "m", 'н' => "n",
                'о' => "o", 'п' => "p", 'р' => "r", 'с' => "s", 'т' => "t", 'у' => "u", 'ф' => "f", 'х' => "h", 'ц' => "ts",
                'ч' => "ch", 'ш' => "sh", 'щ' => "sch", 'ъ' or 'ь' => "", 'ы' => "y", 'э' => "e", 'ю' => "yu", 'я' => "ya",
                _ => c.ToString(),
            });
        // spellings the Latin side varies in: "kh"/"h", "j"/"y", "x"/"ks"
        return sb.Replace("kh", "h").Replace('j', 'y').Replace("x", "ks").ToString();
    }

    /// <summary>
    /// What players put around a song's name, taken away: "(Official Video)", "(feat. X)", "(prod. by Y)",
    /// "[Explicit]", "(OST …)", " - Remastered", emoji; "Artist - Song" and "Artist — Song" titles (YouTube, VK in
    /// a browser) are split into artist and song.
    /// </summary>
    public static (string artist, string title) Clean(string artist, string title)
    {
        title = NoEmoji(title); artist = NoEmoji(artist);
        var pipe = title.IndexOf(" | ", StringComparison.Ordinal);   // "Song | NCS Release"
        if (pipe > 0) title = title[..pipe];
        title = Junk().Replace(title, "").Trim();
        title = Extra().Replace(title, "").Trim();
        title = Featuring().Replace(title, "").Trim();               // "(feat. X)" confuses search
        title = VersionSuffix().Replace(title, "").Trim();   // "Song - Remastered 2011" -> "Song"
        artist = artist.Replace(" - Topic", "", StringComparison.OrdinalIgnoreCase).Trim();

        // YouTube or VK in a browser: artist = channel name, title = "Artist - Song" (or with a long dash)
        var m = TitleDash().Match(title);
        if (m.Success)
        {
            var left = title[..m.Index].Trim();
            var right = title[(m.Index + m.Length)..].Trim();
            if (left.Length > 0 && right.Length > 0)
            {
                artist = left;
                title = right;
            }
        }
        return (Spaces().Replace(artist, " ").Trim(), Spaces().Replace(title, " ").Trim());
    }

    private static string NoEmoji(string s) =>
        new(s.Where(c => !char.IsSurrogate(c) && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.OtherSymbol && c != (char)0xFE0F).ToArray());   // FE0F: "show as emoji"

    private static string MainArtist(string artist)
    {
        var cut = MainArtistSplit().Split(artist);
        return cut.Length > 0 ? cut[0].Trim() : artist;
    }

    private static string E(string s) => Uri.EscapeDataString(s);

    [GeneratedRegex(@"\[(\d+):(\d+(?:[.:]\d+)?)\]")]
    private static partial Regex TimeTag();

    [GeneratedRegex(@"^\s*\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetTag();

    [GeneratedRegex(@"\s*[\(\[][^\)\]]*(official|video|audio|lyric|clip|клип|visuali[sz]er|remaster|\bmv\b|\bhd\b|4k)[^\)\]]*[\)\]]",
        RegexOptions.IgnoreCase)]
    private static partial Regex Junk();

    [GeneratedRegex(@"\s*[\(\[](?:prod\.?|produced|explicit|ost|саундтрек|из (?:к/ф|фильма|сериала))(?![a-zа-я])[^\)\]]*[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex Extra();

    [GeneratedRegex(@"\s+[-–—]\s+")]
    private static partial Regex TitleDash();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\s*[\(\[](?:feat\.?|ft\.?|featuring|при уч\.)\s[^\)\]]*[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex Featuring();

    [GeneratedRegex(@"\s+-\s+[^-]*(remaster|live|version|edit|mix|mono|stereo|acoustic|demo)[^-]*$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionSuffix();

    [GeneratedRegex(@"\s+(?:feat\.?|ft\.?|featuring|x|&)\s+|,\s*", RegexOptions.IgnoreCase)]
    private static partial Regex MainArtistSplit();
}

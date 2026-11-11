using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DynamicIsland.Services;

public sealed record LyricLine(TimeSpan Time, string Text);

/// <param name="Synced">true = each line has a timestamp; false = plain text only.</param>
public sealed record Lyrics(IReadOnlyList<LyricLine> Lines, bool Synced);

/// <summary>Lyrics from LRCLIB (lrclib.net): free, no API key, time-synced LRC.</summary>
public sealed partial class LyricsService
{
    private static readonly HttpClient Http = CreateClient();
    private readonly Dictionary<string, Lyrics?> _cache = new();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DynamicIsland/1.0");
        return http;
    }

    public async Task<Lyrics?> GetAsync(string artist, string title, TimeSpan duration)
    {
        (artist, title) = Clean(artist, title);
        if (title.Length == 0) return null;

        var cacheKey = $"{artist}\n{title}".ToLowerInvariant();
        if (_cache.TryGetValue(cacheKey, out var cached)) return cached;

        try
        {
            Lyrics? result = null;

            // 1) Exact match (needs artist + duration).
            if (artist.Length > 0 && duration > TimeSpan.Zero)
            {
                var url = $"https://lrclib.net/api/get?track_name={E(title)}&artist_name={E(artist)}" +
                          $"&duration={(int)Math.Round(duration.TotalSeconds)}";
                using var doc = await TryGetJsonAsync(url);
                if (doc != null) result = FromRecord(doc.RootElement);
            }

            // 2) Fuzzy search, preferring synced results.
            if (result == null)
            {
                var mainArtist = MainArtist(artist);
                var url = mainArtist.Length > 0
                    ? $"https://lrclib.net/api/search?track_name={E(title)}&artist_name={E(mainArtist)}"
                    : $"https://lrclib.net/api/search?q={E(title)}";
                using var doc = await TryGetJsonAsync(url);
                if (doc != null && doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var items = doc.RootElement.EnumerateArray().ToList();
                    var best = items.FirstOrDefault(i => HasText(i, "syncedLyrics"));
                    if (best.ValueKind == JsonValueKind.Undefined)
                        best = items.FirstOrDefault(i => HasText(i, "plainLyrics"));
                    if (best.ValueKind != JsonValueKind.Undefined) result = FromRecord(best);
                }
            }

            _cache[cacheKey] = result;
            return result;
        }
        catch
        {
            return null; // network problem: don't cache, try again next time
        }
    }

    private static async Task<JsonDocument?> TryGetJsonAsync(string url)
    {
        using var response = await Http.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
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
        return null; // instrumental or empty
    }

    public static List<LyricLine> ParseLrc(string lrc)
    {
        var result = new List<LyricLine>();
        foreach (var raw in lrc.Split('\n'))
        {
            var stamps = TimeTag().Matches(raw);
            if (stamps.Count == 0) continue;
            var text = TimeTag().Replace(raw, "").Trim();
            foreach (Match m in stamps)
            {
                var minutes = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                var seconds = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                result.Add(new LyricLine(TimeSpan.FromSeconds(minutes * 60 + seconds), text));
            }
        }
        result.Sort((a, b) => a.Time.CompareTo(b.Time));
        return result;
    }

    /// <summary>Strip "(Official Video)" etc.; split "Artist - Song" titles from YouTube.</summary>
    private static (string artist, string title) Clean(string artist, string title)
    {
        var pipe = title.IndexOf(" | ", StringComparison.Ordinal);   // "Song | NCS Release"
        if (pipe > 0) title = title[..pipe];
        title = Junk().Replace(title, "").Trim();
        title = Featuring().Replace(title, "").Trim();               // "(feat. X)" confuses search
        title = VersionSuffix().Replace(title, "").Trim();   // "Song - Remastered 2011" -> "Song"
        artist = artist.Replace(" - Topic", "", StringComparison.OrdinalIgnoreCase).Trim();

        // YouTube in a browser: artist = channel name, title = "Artist - Song".
        var dash = title.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0)
        {
            var left = title[..dash].Trim();
            var right = title[(dash + 3)..].Trim();
            if (left.Length > 0 && right.Length > 0)
            {
                artist = left;
                title = right;
            }
        }
        return (artist, title);
    }

    private static string MainArtist(string artist)
    {
        var cut = MainArtistSplit().Split(artist);
        return cut.Length > 0 ? cut[0].Trim() : artist;
    }

    private static string E(string s) => Uri.EscapeDataString(s);

    [GeneratedRegex(@"\[(\d+):(\d+(?:\.\d+)?)\]")]
    private static partial Regex TimeTag();

    [GeneratedRegex(@"\s*[\(\[][^\)\]]*(official|video|audio|lyric|clip|клип|visuali[sz]er|remaster|\bmv\b|\bhd\b|4k)[^\)\]]*[\)\]]",
        RegexOptions.IgnoreCase)]
    private static partial Regex Junk();

    [GeneratedRegex(@"\s*[\(\[](?:feat\.?|ft\.?|featuring|при уч\.)\s[^\)\]]*[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex Featuring();

    [GeneratedRegex(@"\s+-\s+[^-]*(remaster|live|version|edit|mix|mono|stereo|acoustic|demo)[^-]*$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionSuffix();

    [GeneratedRegex(@"\s+(?:feat\.?|ft\.?|featuring|x|&)\s+|,\s*", RegexOptions.IgnoreCase)]
    private static partial Regex MainArtistSplit();
}

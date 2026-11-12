using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace DynamicIsland.Services;

/// <summary>
/// Updates from the project's GitHub releases. Checks the latest release, compares versions,
/// downloads the installer (only from GitHub, verified against the SHA-256 digest GitHub publishes)
/// and runs it silently; the installer closes the island, updates it in place and starts it again.
/// A portable copy (no installer next to it) just opens the release page.
/// </summary>
public static class Updater
{
    public const string Repo = "Fuwni/dynamic-island-windows";
    public static string ReleasesPage => $"https://github.com/{Repo}/releases/latest";

    public sealed record UpdateInfo(Version Version, string Tag, string? SetupUrl, long SetupSize, string? Sha256, string PageUrl);

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Atoll-Updater");   // GitHub requires a user agent; nothing personal in it
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    public static Version Current
    {
        get
        {
            // ATOLL_FAKE_VERSION=1.0.0 pretends to be an older build (for testing the updater)
            if (Version.TryParse(Environment.GetEnvironmentVariable("ATOLL_FAKE_VERSION"), out var fake)) return fake;
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    /// <summary>True when this copy was installed with the setup (so it can update itself).</summary>
    public static bool CanSelfUpdate => Uninstaller.InstalledUninstaller != null && !Uninstaller.IsDevBuild;

    /// <summary>The newer release, or null when this is the latest.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(await Http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct));
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)) return null;
        latest = new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build));
        if (latest <= Current) return null;

        string? url = null, sha = null;
        long size = 0;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (!name.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase)) continue;
            url = asset.GetProperty("browser_download_url").GetString();
            size = asset.GetProperty("size").GetInt64();
            if (asset.TryGetProperty("digest", out var d) && d.GetString() is string digest && digest.StartsWith("sha256:"))
                sha = digest["sha256:".Length..];
            break;
        }
        var page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? ReleasesPage : ReleasesPage;
        return new UpdateInfo(latest, tag, url, size, sha, page);
    }

    /// <summary>Download the installer to a temp file and verify it. Reports 0..1.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken ct = default)
    {
        if (info.SetupUrl == null || !Uri.TryCreate(info.SetupUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("No installer in this release");

        var path = Path.Combine(Path.GetTempPath(), $"Atoll-{info.Version}-setup.exe");
        using (var resp = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? info.SetupSize;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(path);
            var buf = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                if (total > 0) progress?.Report((double)done / total);
            }
        }

        // the file must be exactly what GitHub says it published
        if (info.Sha256 != null)
        {
            await using var f = File.OpenRead(path);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(f, ct));
            if (!hash.Equals(info.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
                throw new InvalidOperationException("The downloaded installer failed the integrity check");
            }
        }
        return path;
    }

    /// <summary>Run the installer quietly; it closes this app, updates it and starts it again.</summary>
    public static void Install(string setupPath) =>
        Process.Start(new ProcessStartInfo(setupPath, "/SILENT /SUPPRESSMSGBOXES /NORESTART /RELAUNCH=1") { UseShellExecute = true });
}

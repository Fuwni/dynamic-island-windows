using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace DynamicIsland.Services;

/// <summary>
/// What a site needs when it does not tell Windows what it is playing (VK Музыка): a small page shim that
/// reports the track to the system media controls, its own CSS, and a network filter for the service's ads.
/// The shim itself is a file, not code: %LOCALAPPDATA%\DynamicIsland\music-extras\{site}.js wins over the copy
/// shipped next to the program, so a broken selector can be fixed without rebuilding and survives updates.
/// </summary>
public static class MusicWebExtras
{
    /// <summary>The user's folder: its files are read before the ones that came with the program.</summary>
    public static string UserFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", "music-extras");

    /// <summary>Ad hosts that are never fetched; ad-hosts.txt in the same folder appends to this list.</summary>
    private static readonly string[] DefaultAdHosts =
    [
        "mradx.net", "ad.mail.ru", "ads.vk.com", "advert", "doubleclick.net",
        "googlesyndication.com", "adservice.google", "an.yandex.ru", "adriver.ru",
    ];

    private static int _blockedLogged;

    /// <summary>
    /// Blocks the service's ads at the network level: the ad file is never fetched, so the player has
    /// nothing to play and moves on to the next track. Muting or skipping an insert that is already
    /// playing does not work — the site's player owns it.
    /// </summary>
    private static void BlockAds(CoreWebView2 core)
    {
        var hosts = new List<string>(DefaultAdHosts);
        var extra = Read("ad-hosts", ".txt");
        if (extra.Length > 0)
            hosts.AddRange(extra.Split(['\n', '\r', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        hosts = hosts.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var host in hosts)
        {
            try { core.AddWebResourceRequestedFilter($"*{host}*", CoreWebView2WebResourceContext.All); }
            catch (Exception ex) { Log.Write($"music extras: filter {host}: {ex.GetType().Name}"); }
        }

        core.WebResourceRequested += (_, e) =>
        {
            try
            {
                e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Blocked", "");
                if (Interlocked.Increment(ref _blockedLogged) <= 30) Log.Write("ad blocked: " + e.Request.Uri);
            }
            catch (Exception ex) { Log.Write("ad blocked: " + ex.GetType().Name + ": " + ex.Message); }
        };
        Log.Write("music extras: ads are blocked: " + string.Join(", ", hosts));
    }

    /// <summary>Attaches the site's shim, its CSS and the ad filter to a music WebView2. Failures go to the log.</summary>
    public static async Task AttachAsync(CoreWebView2 core, string siteId)
    {
        try
        {
            if (siteId == "vk")
            {
                BlockAds(core);

                // the shim reports what it sees (track, cover, position) into log.txt: no DevTools needed
                core.Settings.IsWebMessageEnabled = true;
                core.WebMessageReceived += (_, e) =>
                {
                    try { Log.Write($"{siteId} shim: " + e.TryGetWebMessageAsString()); }
                    catch (Exception ex) { Log.Write($"{siteId} shim: " + ex.GetType().Name); }
                };
            }

            var js = Read(siteId, ".js");
            if (js.Length > 0) await core.AddScriptToExecuteOnDocumentCreatedAsync(js);

            var css = Read(siteId, ".css");
            if (css.Length > 0) await core.AddScriptToExecuteOnDocumentCreatedAsync(CssInjector(css));
        }
        catch (Exception ex) { Log.Write($"music extras: {siteId}: {ex.GetType().Name}: {ex.Message}"); }
    }

    /// <summary>The file from the user's folder, else the one shipped with the program; empty when there is none.</summary>
    internal static string Read(string siteId, string extension)
    {
        foreach (var dir in Folders())
        {
            try
            {
                var path = Path.Combine(dir, siteId + extension);
                if (File.Exists(path)) return File.ReadAllText(path);
            }
            catch (Exception ex) { Log.Write($"music extras: read {dir}: {ex.GetType().Name}: {ex.Message}"); }
        }
        return "";
    }

    internal static IEnumerable<string> Folders()
    {
        yield return UserFolder;
        yield return Path.Combine(AppContext.BaseDirectory, "Assets", "music-extras");
        yield return Path.Combine(AppContext.BaseDirectory, "music-extras");
    }

    /// <summary>CSS as a script that appends a style element: WebView2 has no CSS injection of its own.</summary>
    internal static string CssInjector(string css) =>
        "(function(){var css=" + JsonSerializer.Serialize(css) + ";" +
        "function put(){try{var s=document.createElement('style');s.textContent=css;" +
        "(document.head||document.documentElement).appendChild(s);}catch(e){}}" +
        "if(document.head||document.documentElement)put();else document.addEventListener('DOMContentLoaded',put,{once:true});" +
        "})();";
}

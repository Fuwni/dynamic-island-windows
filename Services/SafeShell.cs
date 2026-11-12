using System.Diagnostics;

namespace DynamicIsland.Services;

/// <summary>
/// Opening links safely. Text from a QR code, a screenshot or a web page is untrusted:
/// handing it straight to the shell could start a program or a custom protocol handler.
/// Only plain web addresses (http / https) are ever opened.
/// </summary>
public static class SafeShell
{
    public static bool IsWebUrl(string? text, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(text) || !Uri.TryCreate(text.Trim(), UriKind.Absolute, out var u)) return false;
        if (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp) return false;
        uri = u;
        return true;
    }

    public static bool IsWebUrl(string? text) => IsWebUrl(text, out _);

    /// <summary>Open a web page in the default browser. Anything that is not http(s) is ignored.</summary>
    public static bool OpenUrl(string? text)
    {
        if (!IsWebUrl(text, out var uri)) return false;
        try
        {
            Process.Start(new ProcessStartInfo(uri!.AbsoluteUri) { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    /// <summary>True when the address is on one of these sites (or their subdomains) over https.</summary>
    public static bool IsOnHost(string? url, IEnumerable<string> hosts) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps &&
        hosts.Any(h => u.Host.Equals(h, StringComparison.OrdinalIgnoreCase) ||
                       u.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));
}

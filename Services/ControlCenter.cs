using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Windows.Devices.Radios;
using Windows.Networking.Connectivity;

namespace DynamicIsland.Services;

/// <summary>
/// The quick toggles of the control centre: Wi-Fi and Bluetooth radios, "airplane" (all radios),
/// Windows VPN connections and power. Everything runs with the user's rights, no admin needed.
/// </summary>
public static class ControlCenter
{
    // ------------------------------------------------------------------ radios

    private static bool _accessAsked;

    private static async Task<IReadOnlyList<Radio>> RadiosAsync()
    {
        if (!_accessAsked)
        {
            _accessAsked = true;
            try { await Radio.RequestAccessAsync(); } catch { }
        }
        try { return await Radio.GetRadiosAsync(); }
        catch { return []; }
    }

    /// <summary>null = this PC has no such radio.</summary>
    public static async Task<bool?> IsOnAsync(RadioKind kind)
    {
        var radios = (await RadiosAsync()).Where(r => r.Kind == kind).ToList();
        return radios.Count == 0 ? null : radios.Any(r => r.State == RadioState.On);
    }

    public static async Task<bool> SetAsync(RadioKind kind, bool on)
    {
        bool ok = false;
        foreach (var r in (await RadiosAsync()).Where(r => r.Kind == kind))
        {
            try { ok |= await r.SetStateAsync(on ? RadioState.On : RadioState.Off) == RadioAccessStatus.Allowed; }
            catch (Exception e) { Log.Write($"radio {kind}: {e.Message}"); }
        }
        return ok;
    }

    /// <summary>"Airplane": every radio off; turning it off brings them back.</summary>
    public static async Task<bool?> AirplaneAsync()
    {
        var radios = (await RadiosAsync()).Where(r => r.Kind is RadioKind.WiFi or RadioKind.Bluetooth or RadioKind.MobileBroadband).ToList();
        return radios.Count == 0 ? null : radios.All(r => r.State != RadioState.On);
    }

    public static async Task SetAirplaneAsync(bool on)
    {
        foreach (var kind in new[] { RadioKind.WiFi, RadioKind.Bluetooth, RadioKind.MobileBroadband })
            await SetAsync(kind, !on);
    }

    // ------------------------------------------------------------------ network

    /// <summary>Name of the network we're online through (Wi-Fi name or "Ethernet"), or null when offline.</summary>
    public static string? NetworkName()
    {
        try
        {
            var p = NetworkInformation.GetInternetConnectionProfile();
            if (p == null || p.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.None) return null;
            return p.IsWlanConnectionProfile ? p.WlanConnectionProfileDetails?.GetConnectedSsid() ?? p.ProfileName : p.ProfileName;
        }
        catch { return null; }
    }

    // ------------------------------------------------------------------ VPN (built-in Windows VPN profiles)

    /// <summary>VPN profiles the user created in Windows settings (rasphone phone book).</summary>
    public static List<string> VpnProfiles()
    {
        var names = new List<string>();
        foreach (var pbk in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Network\Connections\Pbk\rasphone.pbk"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Network\Connections\Pbk\rasphone.pbk"),
                 })
        {
            try
            {
                if (!File.Exists(pbk)) continue;
                foreach (Match m in Regex.Matches(File.ReadAllText(pbk), @"^\[(.+)\]\s*$", RegexOptions.Multiline))
                    if (!names.Contains(m.Groups[1].Value)) names.Add(m.Groups[1].Value);
            }
            catch { }
        }
        return names;
    }

    private static string Rasdial(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("rasdial.exe", args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                StandardOutputEncoding = System.Text.Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
            };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(20000);
            return output;
        }
        catch { return ""; }
    }

    /// <summary>The connected VPN profile, if any.</summary>
    public static string? ConnectedVpn()
    {
        var profiles = VpnProfiles();
        if (profiles.Count == 0) return null;
        var output = Rasdial("");
        return profiles.FirstOrDefault(n => output.Contains(n, StringComparison.OrdinalIgnoreCase));
    }

    public static Task<bool> ConnectVpnAsync(string name) => Task.Run(() =>
    {
        Rasdial($"\"{name}\"");
        return ConnectedVpn() == name;
    });

    public static Task DisconnectVpnAsync(string name) => Task.Run(() => Rasdial($"\"{name}\" /disconnect"));

    /// <summary>Windows' own VPN page (for adding a profile or for third-party VPN apps).</summary>
    // VPN apps that don't create a Windows VPN profile (proxy / TUN clients): the tile opens them instead
    private static readonly string[] VpnApps =
        ["Happ", "Hiddify", "v2rayN", "AmneziaVPN", "Amnezia", "Outline", "WireGuard", "NekoRay", "Nekobox", "Throne",
         "Clash Verge", "Karing", "Streisand", "Proton VPN", "Windscribe", "Mullvad VPN", "Cloudflare WARP"];
    private static (string Name, string Path)? _vpnApp;
    private static DateTime _vpnAppAt;

    /// <summary>An installed VPN client found by its Start-menu shortcut (re-checked every minute).</summary>
    public static (string Name, string Path)? VpnApp()
    {
        if (DateTime.UtcNow - _vpnAppAt < TimeSpan.FromMinutes(1)) return _vpnApp;
        _vpnAppAt = DateTime.UtcNow;
        _vpnApp = null;
        try
        {
            var roots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                                Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) };
            var links = roots.Where(Directory.Exists)
                .SelectMany(r => { try { return Directory.EnumerateFiles(r, "*.lnk", SearchOption.AllDirectories); } catch { return []; } })
                .ToList();
            foreach (var app in VpnApps)
            {
                var hit = links.FirstOrDefault(l => Path.GetFileNameWithoutExtension(l).StartsWith(app, StringComparison.OrdinalIgnoreCase)
                                                    && !Path.GetFileNameWithoutExtension(l).Contains("uninstall", StringComparison.OrdinalIgnoreCase));
                if (hit != null) { _vpnApp = (app, hit); break; }
            }
        }
        catch { }
        return _vpnApp;
    }

    public static void OpenVpnApp(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    public static void OpenVpnSettings() => Process.Start(new ProcessStartInfo("ms-settings:network-vpn") { UseShellExecute = true });

    // ------------------------------------------------------------------ power

    public static void Sleep() => System.Windows.Forms.Application.SetSuspendState(System.Windows.Forms.PowerState.Suspend, false, false);

    public static void Restart() => Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0") { UseShellExecute = false, CreateNoWindow = true });

    public static void ShutDown() => Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 0") { UseShellExecute = false, CreateNoWindow = true });

    /// <summary>Battery percent and charging, or null on a desktop PC.</summary>
    public static (int Percent, bool Charging)? Battery()
    {
        var s = System.Windows.Forms.SystemInformation.PowerStatus;
        if (s.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery) || s.BatteryLifePercent > 1.0f) return null;
        return ((int)Math.Round(s.BatteryLifePercent * 100), s.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online);
    }
}

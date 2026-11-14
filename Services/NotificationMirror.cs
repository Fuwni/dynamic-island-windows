using System.IO;
using System.Windows.Media.Imaging;
using global::Windows.UI.Notifications;
using global::Windows.UI.Notifications.Management;

namespace DynamicIsland.Services;

/// <summary>
/// Reads Windows notifications (with the user's permission) so messages from Telegram, Discord, Viber…
/// can be shown on the island.
/// </summary>
public sealed class NotificationMirror
{
    public sealed record Message(string App, string Title, string Body, BitmapImage? Logo);

    private static readonly string[] Messengers =
        ["telegram", "discord", "viber", "whatsapp", "signal", "slack", "teams", "skype", "vk", "messenger", "element", "zoom"];

    private readonly HashSet<uint> _seen = [];
    private bool _primed;
    private UserNotificationListener? _listener;

    public bool Available { get; private set; }

    /// <summary>
    /// The Windows notification center is switched off by a policy (often set by "tweak" tools): Windows then
    /// keeps no notifications at all, so there is nothing to read.
    /// </summary>
    public static bool CenterDisabled()
    {
        foreach (var root in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
        {
            try
            {
                using var k = root.OpenSubKey(@"Software\Policies\Microsoft\Windows\Explorer");
                if (k?.GetValue("DisableNotificationCenter") is int v && v != 0) return true;
            }
            catch { }
        }
        return false;
    }

    public async Task<bool> InitAsync()
    {
        try
        {
            _listener = UserNotificationListener.Current;
            var status = await _listener.RequestAccessAsync();
            Available = status == UserNotificationListenerAccessStatus.Allowed;
        }
        catch { Available = false; }
        return Available;
    }

    /// <summary>Test: logs what the listener sees, step by step.</summary>
    public async Task DebugPollAsync()
    {
        try
        {
            var list = await _listener!.GetNotificationsAsync(NotificationKinds.Toast);
            foreach (var n in list)
            {
                if (!_seen.Add(n.Id)) continue;
                string app = "?", step = "appinfo";
                try
                {
                    app = n.AppInfo?.DisplayInfo?.DisplayName ?? "(null)";
                    step = "binding";
                    var b = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
                    var texts = b?.GetTextElements().Select(t => t.Text).ToList() ?? [];
                    Log.Write($"notiftest: #{n.Id} app='{app}' texts={texts.Count}");
                }
                catch (Exception e) { Log.Write($"notiftest: #{n.Id} app='{app}' failed at {step}: {e.GetType().Name} {e.Message}"); }
            }
        }
        catch (Exception e) { Log.Write("notiftest: list failed: " + e.Message); }
    }

    /// <summary>New notifications since the last call (the first call only remembers what is already there).</summary>
    public async Task<List<Message>> PollAsync(bool allApps)
    {
        var result = new List<Message>();
        if (!Available || _listener == null) return result;

        IReadOnlyList<UserNotification> list;
        try { list = await _listener.GetNotificationsAsync(NotificationKinds.Toast); }
        catch { return result; }

        foreach (var n in list)
        {
            if (!_seen.Add(n.Id) || !_primed) continue;
            try
            {
                var app = n.AppInfo?.DisplayInfo?.DisplayName ?? "";
                if (app.Contains("Dynamic Island", StringComparison.OrdinalIgnoreCase) || app.Contains("Atoll", StringComparison.OrdinalIgnoreCase)) continue;
                if (!allApps && !Messengers.Any(m => app.Contains(m, StringComparison.OrdinalIgnoreCase))) continue;

                var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
                var texts = binding?.GetTextElements().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? [];
                var title = texts.FirstOrDefault() ?? app;
                var body = string.Join(" · ", texts.Skip(1));

                BitmapImage? logo = null;
                try
                {
                    var stream = n.AppInfo!.DisplayInfo.GetLogo(new global::Windows.Foundation.Size(64, 64));
                    using var ras = await stream.OpenReadAsync();
                    using var s = ras.AsStreamForRead();
                    var ms = new MemoryStream();
                    await s.CopyToAsync(ms);
                    ms.Position = 0;
                    logo = new BitmapImage();
                    logo.BeginInit();
                    logo.CacheOption = BitmapCacheOption.OnLoad;
                    logo.StreamSource = ms;
                    logo.EndInit();
                    logo.Freeze();
                }
                catch { logo = null; }

                result.Add(new Message(app, title, body, logo));
            }
            catch { }
        }
        _primed = true;
        if (_seen.Count > 500) _seen.IntersectWith(list.Select(n => n.Id));
        return result;
    }
}

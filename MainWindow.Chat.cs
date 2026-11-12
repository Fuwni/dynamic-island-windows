using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace DynamicIsland;

/// <summary>Chat tab: Claude / Gemini / DeepSeek in an embedded browser (sign in once, no API keys).</summary>
public partial class MainWindow
{
    private WebView2CompositionControl? _chat;
    private DateTime _chatTypedAt, _chatSentAt;

    /// <summary>What the chat buddy is doing: typing along, thinking after you send, or just sitting.</summary>
    private Views.CharacterMood ChatBuddyMood()
    {
        var now = DateTime.Now;
        if (now - _chatTypedAt < TimeSpan.FromMilliseconds(700)) return Views.CharacterMood.Typing;
        if (now - _chatSentAt < TimeSpan.FromSeconds(6)) return Views.CharacterMood.Think;
        return Views.CharacterMood.Idle;
    }
    private bool _chatStarting;

    private static string ChatUrl(string provider) => provider switch
    {
        "claude" => "https://claude.ai/new",
        "deepseek" => "https://chat.deepseek.com/",
        _ => "https://gemini.google.com/app",
    };

    // sign-in pages that must stay inside the chat instead of opening the normal browser
    private static readonly string[] ChatHosts =
        ["claude.ai", "anthropic.com", "accounts.google.com", "gemini.google.com", "deepseek.com", "appleid.apple.com"];

    private string SelectedProvider =>
        ChatClaude.IsChecked == true ? "claude" : ChatDeepSeek.IsChecked == true ? "deepseek" : "gemini";

    private async Task EnsureChatAsync()
    {
        if (_chat != null || _chatStarting) return;
        _chatStarting = true;

        (_settings.ChatProvider switch
        {
            "claude" => ChatClaude,
            "deepseek" => ChatDeepSeek,
            _ => ChatGemini,
        }).IsChecked = true;

        try
        {
            var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", "WebView2");
            // Google's "device bound sessions" tie a login to a hardware key that embedded browsers
            // can't always re-prove after a restart - which looks like being logged out. Plain cookies instead.
            var options = new CoreWebView2EnvironmentOptions(
                "--disable-features=DeviceBoundSessions,EnableBoundSessionCredentials,DeviceBoundSessionsRefreshQuota");
            var env = await CoreWebView2Environment.CreateAsync(null, dataDir, options);

            // composition control renders inside our transparent window (the classic one can't)
            _chat = new WebView2CompositionControl { Visibility = Visibility.Hidden };
            ChatHost.Children.Add(_chat);
            ChatHost.SizeChanged += (_, _) =>
                ChatHost.Clip = new RectangleGeometry(new Rect(0, 0, ChatHost.ActualWidth, ChatHost.ActualHeight), 19, 19);

            await _chat.EnsureCoreWebView2Async(env);
            var core = _chat.CoreWebView2;
            // tell the island when you type (only that a key was pressed - never what)
            await core.AddScriptToExecuteOnDocumentCreatedAsync(
                "addEventListener('keydown',e=>{try{chrome.webview.postMessage(e.key==='Enter'&&!e.shiftKey?'enter':'key')}catch(_){}},true);");
            core.WebMessageReceived += (_, e) =>
            {
                string? m = null;
                try { m = e.TryGetWebMessageAsString(); } catch { }
                if (!Services.SafeShell.IsOnHost(e.Source, ChatHosts)) return;   // only from the chat sites
                if (m == "enter") _chatSentAt = DateTime.Now;
                else if (m == "key") _chatTypedAt = DateTime.Now;
            };
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            // never let a page start a program through a custom protocol (ms-..., file:, etc.)
            core.NavigationStarting += (_, e) =>
            {
                var u = e.Uri ?? "";
                if (!(u.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || u.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                      || u.StartsWith("about:", StringComparison.OrdinalIgnoreCase) || u.StartsWith("data:", StringComparison.OrdinalIgnoreCase)))
                    e.Cancel = true;
            };
            core.Settings.IsStatusBarEnabled = false;
            core.NewWindowRequested += (_, e) =>
            {
                var uri = e.Uri ?? "";
                if (Services.SafeShell.IsOnHost(uri, ChatHosts)) return; // sign-in popup stays inside
                e.Handled = true;
                Services.SafeShell.OpenUrl(uri);                          // other links: the normal browser, web pages only
            };
            core.NavigationCompleted += async (_, _) =>
            {
                if (Environment.GetCommandLineArgs().Contains("--cookie-debug"))
                {
                    try
                    {
                        var cookies = await core.CookieManager.GetCookiesAsync(core.Source);
                        Services.Log.Write($"cookies for {new Uri(core.Source).Host}: " + string.Join(", ",
                            cookies.Select(c => $"{c.Name}[{(c.IsSession ? "session" : c.Expires.ToString("MM-dd"))}]")));
                    }
                    catch (Exception ex) { Services.Log.Write("cookie debug: " + ex.Message); }
                }
                ChatPlaceholder.Visibility = Visibility.Collapsed;
                if (_chat.Visibility != Visibility.Visible)
                {
                    _chat.Visibility = Visibility.Visible;
                    Reveal(_chat, 0.97, 0);
                }
            };
            core.Navigate(ChatUrl(_settings.ChatProvider));
        }
        catch (Exception ex)
        {
            _chat = null;
            ChatPlaceholder.Text = L.T("Не удалось открыть чат: ") + ex.Message;
        }
        finally { _chatStarting = false; }
    }

    private void ChatProvider_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        var provider = SelectedProvider;
        if (provider == _settings.ChatProvider && _chat?.CoreWebView2?.Source.Contains(new Uri(ChatUrl(provider)).Host) == true) return;
        _settings.ChatProvider = provider;
        _settings.Save();
        Kick(2);
        _chat?.CoreWebView2?.Navigate(ChatUrl(provider));
    }

    /// <summary>
    /// Close the embedded browser properly so it writes its cookies to disk.
    /// Without this a Windows shutdown kills it mid-flight and recent logins are lost.
    /// </summary>
    private void ShutdownChat()
    {
        var chat = _chat;
        _chat = null;
        if (chat == null) return;
        try
        {
            var browserPid = (int)chat.CoreWebView2.BrowserProcessId;
            ChatHost.Children.Remove(chat);
            chat.Dispose();
            // give the browser process a moment to flush and exit by itself
            using var p = Process.GetProcessById(browserPid);
            p.WaitForExit(2500);
        }
        catch { }
    }

    private void ChatReload_Click(object sender, RoutedEventArgs e) => _chat?.CoreWebView2?.Reload();

    private void ChatOpenExternal_Click(object sender, RoutedEventArgs e)
    {
        var url = _chat?.CoreWebView2?.Source ?? ChatUrl(_settings.ChatProvider);
        Services.SafeShell.OpenUrl(url);
    }
}

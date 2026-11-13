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
    private bool _chatHostHooked;
    private DateTime _chatSeenAt = DateTime.Now;
    private readonly System.Windows.Threading.DispatcherTimer _chatIdleTimer = new() { Interval = TimeSpan.FromMinutes(1) };

    /// <summary>
    /// The embedded browser takes several hundred MB. After 10 minutes away from the chat it is closed
    /// (the login stays: cookies are on disk) and opens again the next time you look at the chat.
    /// </summary>
    private void ReleaseIdleChat()
    {
        if (_chat == null || _chatStarting) return;
        if (_mode == Mode.Expanded && TabChat.IsChecked == true) { _chatSeenAt = DateTime.Now; return; }
        if (DateTime.Now - _chatSeenAt < TimeSpan.FromMinutes(10)) return;
        var chat = _chat;
        _chat = null;
        try
        {
            ChatHost.Children.Remove(chat);
            chat.Dispose();
        }
        catch { }
        ChatPlaceholder.Visibility = Visibility.Visible;
        _chatIdleTimer.Stop();
    }

    public sealed record ChatProvider(string Id, string Name, string Hint, string Url);

    /// <summary>The AI chats the island can show (their own websites, with your own account).</summary>
    private static readonly ChatProvider[] ChatProviders =
    [
        new("chatgpt", "ChatGPT", "OpenAI", "https://chatgpt.com/"),
        new("claude", "Claude", "Anthropic", "https://claude.ai/new"),
        new("gemini", "Gemini", "Google", "https://gemini.google.com/app"),
        new("deepseek", "DeepSeek", "DeepSeek", "https://chat.deepseek.com/"),
        new("grok", "Grok", "xAI", "https://grok.com/"),
        new("perplexity", "Perplexity", "Perplexity AI", "https://www.perplexity.ai/"),
        new("copilot", "Copilot", "Microsoft", "https://copilot.microsoft.com/"),
        new("mistral", "Le Chat", "Mistral AI", "https://chat.mistral.ai/chat"),
        new("qwen", "Qwen", "Alibaba", "https://chat.qwen.ai/"),
    ];

    private static ChatProvider ProviderOf(string id) => ChatProviders.FirstOrDefault(p => p.Id == id) ?? ChatProviders[2];
    private static string ChatUrl(string provider) => ProviderOf(provider).Url;

    // sign-in pages that must stay inside the chat instead of opening the normal browser
    private static readonly string[] ChatHosts =
    [
        "claude.ai", "anthropic.com", "accounts.google.com", "gemini.google.com", "deepseek.com", "appleid.apple.com",
        "chatgpt.com", "openai.com", "grok.com", "x.ai", "perplexity.ai", "copilot.microsoft.com",
        "login.live.com", "login.microsoftonline.com", "account.live.com", "mistral.ai", "qwen.ai",
    ];

    private string SelectedProvider => (ChatProviderBox.SelectedItem as ChatProvider)?.Id ?? _settings.ChatProvider;

    private void FillChatProviders()
    {
        if (ChatProviderBox.Items.Count > 0) return;
        _chatProvidersLoading = true;
        foreach (var p in ChatProviders) ChatProviderBox.Items.Add(p);
        ChatProviderBox.SelectedItem = ProviderOf(_settings.ChatProvider);
        _chatProvidersLoading = false;
    }
    private bool _chatProvidersLoading;

    private async Task EnsureChatAsync()
    {
        _chatSeenAt = DateTime.Now;
        if (_chat != null || _chatStarting) return;
        _chatStarting = true;
        if (!_chatIdleTimer.IsEnabled)
        {
            _chatIdleTimer.Tick -= ChatIdleTick;
            _chatIdleTimer.Tick += ChatIdleTick;
            _chatIdleTimer.Start();
        }

        FillChatProviders();
        ApplyChatSize();

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
            if (!_chatHostHooked)
            {
                _chatHostHooked = true;
                ChatHost.SizeChanged += (_, _) =>
                    ChatHost.Clip = new RectangleGeometry(new Rect(0, 0, ChatHost.ActualWidth, ChatHost.ActualHeight), 19, 19);
            }

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
            core.Settings.IsPasswordAutosaveEnabled = false;   // the chat window is not a password manager
            // a page in the chat can't drop a program, script or shortcut onto the PC
            core.DownloadStarting += (_, e) =>
            {
                var name = System.IO.Path.GetFileName(e.ResultFilePath ?? "").ToLowerInvariant();
                string[] risky = [".exe", ".msi", ".msix", ".appx", ".bat", ".cmd", ".com", ".scr", ".pif", ".ps1", ".psm1", ".vbs", ".vbe",
                                  ".js", ".jse", ".wsf", ".wsh", ".hta", ".lnk", ".url", ".reg", ".jar", ".dll", ".cpl", ".msc", ".iso", ".img", ".vhd", ".vhdx"];
                if (risky.Any(x => name.EndsWith(x)))
                {
                    e.Cancel = true;
                    Dispatcher.BeginInvoke(() => ShowToast("\uE72E", L.T("Загрузка заблокирована"), L.F("Чат пытался скачать «{0}» — программы из чата не скачиваются", name)));
                }
            };
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

    private void ChatIdleTick(object? sender, EventArgs e) => ReleaseIdleChat();

    /// <summary>
    /// Large chat: sign-up pages with a captcha (Qwen, Grok…) don't fit the normal size and their
    /// bottom is cut off by the island's edge.
    /// </summary>
    private void ApplyChatSize()
    {
        bool big = _settings.ChatLarge;
        ChatPage.Width = big ? 860 : 640;
        ChatFrame.Height = big ? 660 : 470;
        ChatSizeButton.Content = big ? "\uE73F" : "\uE740";
        ChatSizeButton.ToolTip = big ? L.T("Обычный размер") : L.T("Больше места (для входа и капчи)");
    }

    private void ChatSize_Click(object sender, RoutedEventArgs e)
    {
        _settings.ChatLarge = !_settings.ChatLarge;
        _settings.Save();
        ApplyChatSize();
        Remeasure();
    }

    private void ChatProvider_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _chatProvidersLoading) return;
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

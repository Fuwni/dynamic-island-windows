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
            var env = await CoreWebView2Environment.CreateAsync(null, dataDir);

            // composition control renders inside our transparent window (the classic one can't)
            _chat = new WebView2CompositionControl { Visibility = Visibility.Hidden };
            ChatHost.Children.Add(_chat);
            ChatHost.SizeChanged += (_, _) =>
                ChatHost.Clip = new RectangleGeometry(new Rect(0, 0, ChatHost.ActualWidth, ChatHost.ActualHeight), 19, 19);

            await _chat.EnsureCoreWebView2Async(env);
            var core = _chat.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.NewWindowRequested += (_, e) =>
            {
                var uri = e.Uri ?? "";
                if (ChatHosts.Any(h => uri.Contains(h, StringComparison.OrdinalIgnoreCase))) return; // sign-in popup
                e.Handled = true;
                try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { }
            };
            core.NavigationCompleted += (_, _) =>
            {
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
            ChatPlaceholder.Text = "Не удалось открыть чат: " + ex.Message;
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

    private void ChatReload_Click(object sender, RoutedEventArgs e) => _chat?.CoreWebView2?.Reload();

    private void ChatOpenExternal_Click(object sender, RoutedEventArgs e)
    {
        var url = _chat?.CoreWebView2?.Source ?? ChatUrl(_settings.ChatProvider);
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}

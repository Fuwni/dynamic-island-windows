using System.Windows;
using DynamicIsland.Services;

namespace DynamicIsland;

/// <summary>Claude Code: answer permission prompts and see "done" messages without switching to the terminal.</summary>
public partial class MainWindow
{
    private readonly ClaudeBridge _bridge = new();
    private readonly Queue<ClaudeBridge.PermissionRequest> _claudeQueue = new();
    private ClaudeBridge.PermissionRequest? _claudeCurrent;
    private string _claudeInfoText = "";

    private void InitClaude()
    {
        _bridge.PermissionRequested += req => Dispatcher.BeginInvoke(() => EnqueuePermission(req));
        _bridge.Notified += n => Dispatcher.BeginInvoke(() => OnClaudeNotice(n));
        _bridge.Start();
        ClaudeHooks.RefreshScript();
        FillChatProviders();
    }

    private void EnqueuePermission(ClaudeBridge.PermissionRequest req)
    {
        _claudeQueue.Enqueue(req);
        // the hook gives up after ~110 s and Claude Code asks in the terminal instead
        _ = req.Decision.Task.ContinueWith(_ => Dispatcher.BeginInvoke(() =>
        {
            if (_claudeCurrent == req) NextPermission();
        }));
        if (_claudeCurrent == null) NextPermission();
        else UpdateQueueLabel();
    }

    private void NextPermission()
    {
        _claudeCurrent = null;
        while (_claudeQueue.Count > 0)
        {
            var req = _claudeQueue.Dequeue();
            if (req.Decision.Task.IsCompleted) continue;
            _claudeCurrent = req;
            break;
        }

        if (_claudeCurrent == null)
        {
            if (_mode == Mode.Claude) SetMode(Mode.Compact);
            return;
        }

        if (_hidden) ShowIsland();
        ShowAgent(_claudeCurrent.Agent);
        ClaudeProject.Text = _claudeCurrent.Project;
        ClaudeTitle.Text = L.F("Хочет использовать «{0}». Разрешить?", _claudeCurrent.Tool);
        ClaudeDetails.Text = _claudeCurrent.Details;
        ClaudeAskButtons.Visibility = Visibility.Visible;
        ClaudeInfoButtons.Visibility = Visibility.Collapsed;
        UpdateQueueLabel();
        SetMode(Mode.Claude);
    }

    private void UpdateQueueLabel() =>
        ClaudeQueue.Text = _claudeQueue.Count > 0 ? L.F("ещё {0} в очереди", _claudeQueue.Count) : "";

    private void Answer(string? behavior)
    {
        _claudeCurrent?.Decision.TrySetResult(behavior);
        NextPermission();
    }

    private void ClaudeAllow_Click(object sender, RoutedEventArgs e) => Answer("allow");
    private void ClaudeDeny_Click(object sender, RoutedEventArgs e) => Answer("deny");
    private void ClaudeTerminal_Click(object sender, RoutedEventArgs e) => Answer(null);

    private void OnClaudeNotice(ClaudeBridge.Notice n)
    {
        // the permission prompt itself is already on the island
        if (n.Kind == "permission_prompt" && _claudeCurrent != null) return;

        bool codex = n.Agent == "Codex";
        var title = n.Kind == "stop" ? (codex ? L.T("Codex закончил") : L.T("Claude Code закончил")) : string.IsNullOrWhiteSpace(n.Title) ? n.Agent : n.Title;
        var project = string.IsNullOrEmpty(n.Project) ? "" : n.Project + " · ";
        var preview = n.Message.Replace('\n', ' ').Trim();

        _claudeInfoText = n.Message;
        ShowToast(codex ? "◎" : "✳", title, project + (preview.Length > 0 ? preview : L.T("готово")), () => ShowClaudeMessage(n),
            codex ? CodexGreen : (System.Windows.Media.Brush)FindResource("ClaudeOrange"), ms: 6000);
    }

    private void ShowClaudeMessage(ClaudeBridge.Notice n)
    {
        if (_claudeCurrent != null || string.IsNullOrWhiteSpace(n.Message)) return;
        ShowAgent(n.Agent);
        ClaudeProject.Text = n.Project;
        ClaudeQueue.Text = "";
        ClaudeTitle.Text = n.Kind == "stop" ? L.T("Последний ответ:") : n.Title;
        ClaudeDetails.Text = n.Message;
        ClaudeAskButtons.Visibility = Visibility.Collapsed;
        ClaudeInfoButtons.Visibility = Visibility.Visible;
        SetMode(Mode.Claude);
    }

    private static readonly System.Windows.Media.Brush CodexGreen =
        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x10, 0xA3, 0x7F));

    /// <summary>The panel shows who is asking: Claude Code (orange ✳) or Codex (green ◎).</summary>
    private void ShowAgent(string agent)
    {
        bool codex = agent == "Codex";
        ClaudeAgentName.Text = codex ? "Codex" : "Claude Code";
        ClaudeAgentIcon.Text = codex ? "◎" : "✳";
        if (codex) ClaudeAgentIcon.Foreground = CodexGreen;
        else ClaudeAgentIcon.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "ClaudeOrange");
    }

    private void ClaudeCopy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(_claudeInfoText); } catch { }
        SetMode(Mode.Compact);
        ShowToast("", L.T("Скопировано"), null);
    }

    private void ClaudeClose_Click(object sender, RoutedEventArgs e) => SetMode(Mode.Compact);

    // -------------------------------------------------------------- hooks in settings.json

    private void RefreshClaudeHooksUi()
    {
        bool installed = ClaudeHooks.IsInstalled;
        ClaudeHooksButton.Content = installed ? L.T("Отключить") : L.T("Подключить");
        ClaudeHooksStatus.Text = installed
            ? (_bridge.IsRunning
                ? L.T("Подключено. Запросы разрешений Claude Code появятся на острове — отвечайте прямо здесь. Если остров закрыт, Claude Code спросит в терминале, как обычно.")
                : L.T("Хуки установлены, но порт 47811 занят — остров не может их принять."))
            : L.T("Остров сможет показывать запросы разрешений и «готово» от Claude Code и отвечать на них без терминала. Добавит хуки в ~/.claude/settings.json (резервная копия сохранится рядом).");
    }

    private void RefreshCodexHooksUi()
    {
        bool installed = CodexHooks.IsInstalled;
        CodexHooksButton.Content = installed ? L.T("Отключить") : L.T("Подключить");
        CodexHooksStatus.Text = installed
            ? L.T("Подключено. Один раз подтвердите хуки в Codex командой /hooks — после этого запросы разрешений и «готово» появятся на острове.")
            : L.T("Запросы разрешений и «готово» от Codex CLI на острове. Добавит хуки в ~/.codex/hooks.json (резервная копия сохранится рядом).");
    }

    private void CodexHooks_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (CodexHooks.IsInstalled) CodexHooks.Uninstall();
            else CodexHooks.Install();
        }
        catch (Exception ex)
        {
            CodexHooksStatus.Text = L.T("Не получилось: ") + ex.Message;
            return;
        }
        RefreshCodexHooksUi();
        Remeasure();
    }

    private void ClaudeHooks_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ClaudeHooks.IsInstalled) ClaudeHooks.Uninstall();
            else ClaudeHooks.Install();
        }
        catch (Exception ex)
        {
            ClaudeHooksStatus.Text = L.T("Не получилось: ") + ex.Message;
            return;
        }
        RefreshClaudeHooksUi();
        Remeasure();
    }
}

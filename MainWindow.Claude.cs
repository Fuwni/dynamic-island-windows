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
        ClaudeProject.Text = _claudeCurrent.Project;
        ClaudeTitle.Text = $"Хочет использовать «{_claudeCurrent.Tool}». Разрешить?";
        ClaudeDetails.Text = _claudeCurrent.Details;
        ClaudeAskButtons.Visibility = Visibility.Visible;
        ClaudeInfoButtons.Visibility = Visibility.Collapsed;
        UpdateQueueLabel();
        SetMode(Mode.Claude);
    }

    private void UpdateQueueLabel() =>
        ClaudeQueue.Text = _claudeQueue.Count > 0 ? $"ещё {_claudeQueue.Count} в очереди" : "";

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

        var title = n.Kind == "stop" ? "Claude Code закончил" : string.IsNullOrWhiteSpace(n.Title) ? "Claude Code" : n.Title;
        var project = string.IsNullOrEmpty(n.Project) ? "" : n.Project + " · ";
        var preview = n.Message.Replace('\n', ' ').Trim();

        _claudeInfoText = n.Message;
        ShowToast("✳", title, project + (preview.Length > 0 ? preview : "готово"), () => ShowClaudeMessage(n),
            (System.Windows.Media.Brush)FindResource("ClaudeOrange"), ms: 6000);
    }

    private void ShowClaudeMessage(ClaudeBridge.Notice n)
    {
        if (_claudeCurrent != null || string.IsNullOrWhiteSpace(n.Message)) return;
        ClaudeProject.Text = n.Project;
        ClaudeQueue.Text = "";
        ClaudeTitle.Text = n.Kind == "stop" ? "Последний ответ:" : n.Title;
        ClaudeDetails.Text = n.Message;
        ClaudeAskButtons.Visibility = Visibility.Collapsed;
        ClaudeInfoButtons.Visibility = Visibility.Visible;
        SetMode(Mode.Claude);
    }

    private void ClaudeCopy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(_claudeInfoText); } catch { }
        SetMode(Mode.Compact);
        ShowToast("", "Скопировано", null);
    }

    private void ClaudeClose_Click(object sender, RoutedEventArgs e) => SetMode(Mode.Compact);

    // -------------------------------------------------------------- hooks in settings.json

    private void RefreshClaudeHooksUi()
    {
        bool installed = ClaudeHooks.IsInstalled;
        ClaudeHooksButton.Content = installed ? "Отключить" : "Подключить";
        ClaudeHooksStatus.Text = installed
            ? (_bridge.IsRunning
                ? "Подключено. Запросы разрешений Claude Code появятся на острове — отвечайте прямо здесь. Если остров закрыт, Claude Code спросит в терминале, как обычно."
                : "Хуки установлены, но порт 47811 занят — остров не может их принять.")
            : "Остров сможет показывать запросы разрешений и «готово» от Claude Code и отвечать на них без терминала. Добавит хуки в ~/.claude/settings.json (резервная копия сохранится рядом).";
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
            ClaudeHooksStatus.Text = "Не получилось: " + ex.Message;
            return;
        }
        RefreshClaudeHooksUi();
        Remeasure();
    }
}

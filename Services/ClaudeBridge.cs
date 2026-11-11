using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DynamicIsland.Services;

/// <summary>
/// Local HTTP endpoint the Claude Code hook script talks to.
/// PermissionRequest waits for the user's click on the island; notifications are fire-and-forget.
/// </summary>
public sealed class ClaudeBridge
{
    public const int Port = 47811;

    public sealed class PermissionRequest
    {
        public required string Tool { get; init; }
        public required string Details { get; init; }
        public required string Project { get; init; }
        public TaskCompletionSource<string?> Decision { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed record Notice(string Kind, string Title, string Message, string Project);

    private HttpListener? _listener;

    /// <summary>Raised on a background thread.</summary>
    public event Action<PermissionRequest>? PermissionRequested;
    public event Action<Notice>? Notified;

    public bool IsRunning => _listener?.IsListening == true;

    public void Start()
    {
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{Port}/");
            _listener.Start();
            _ = Task.Run(LoopAsync);
        }
        catch
        {
            _listener = null; // port busy or not allowed - island works without the bridge
        }
    }

    private async Task LoopAsync()
    {
        while (_listener?.IsListening == true)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { return; }
            _ = Task.Run(() => HandleAsync(ctx));
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        string response = "";
        try
        {
            using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            var json = JsonNode.Parse(body) as JsonObject ?? new JsonObject();
            var project = Path.GetFileName((json["cwd"]?.GetValue<string>() ?? "").TrimEnd('\\', '/'));

            switch (ctx.Request.Url?.AbsolutePath)
            {
                case "/permission":
                {
                    var req = new PermissionRequest
                    {
                        Tool = json["tool_name"]?.GetValue<string>() ?? "?",
                        Details = Describe(json["tool_name"]?.GetValue<string>(), json["tool_input"]),
                        Project = project,
                    };
                    PermissionRequested?.Invoke(req);
                    var winner = await Task.WhenAny(req.Decision.Task, Task.Delay(TimeSpan.FromSeconds(110)));
                    var behavior = winner == req.Decision.Task ? req.Decision.Task.Result : null;
                    req.Decision.TrySetResult(null);

                    if (behavior is "allow" or "deny")
                    {
                        var decision = new JsonObject { ["behavior"] = behavior };
                        if (behavior == "deny") decision["message"] = "The user denied this from Dynamic Island.";
                        response = new JsonObject
                        {
                            ["hookSpecificOutput"] = new JsonObject
                            {
                                ["hookEventName"] = "PermissionRequest",
                                ["decision"] = decision,
                            },
                        }.ToJsonString();
                    }
                    break; // empty response = ask in the terminal as usual
                }
                case "/notify":
                {
                    var evt = json["hook_event_name"]?.GetValue<string>() ?? "";
                    if (evt == "Stop")
                    {
                        var last = json["last_assistant_message"]?.GetValue<string>() ?? "";
                        Notified?.Invoke(new Notice("stop", "Claude Code закончил", last, project));
                    }
                    else
                    {
                        var type = json["notification_type"]?.GetValue<string>() ?? "";
                        var msg = json["message"]?.GetValue<string>() ?? "";
                        Notified?.Invoke(new Notice(type, json["title"]?.GetValue<string>() ?? "Claude Code", msg, project));
                    }
                    break;
                }
            }
        }
        catch { }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(response);
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }
        catch { }
    }

    private static string Describe(string? tool, JsonNode? input)
    {
        if (input is not JsonObject o) return "";
        string? Get(string k) => o[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

        var text = tool switch
        {
            "Bash" or "PowerShell" => Get("command"),
            "Edit" or "Write" or "Read" or "NotebookEdit" => Get("file_path"),
            "WebFetch" => Get("url"),
            "WebSearch" => Get("query"),
            _ => null,
        } ?? o.ToJsonString(new JsonSerializerOptions { WriteIndented = false });

        var desc = Get("description");
        if (!string.IsNullOrWhiteSpace(desc) && desc != text) text = desc + "\n" + text;
        return text.Length > 600 ? text[..600] + "…" : text;
    }
}

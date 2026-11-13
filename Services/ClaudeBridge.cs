using System.IO;
using System.Net;
using System.Security.Cryptography;
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
    private const long MaxBody = 1_000_000;
    private const int MaxPending = 4;
    private int _pending;

    /// <summary>
    /// A random secret only this Windows user can read. The hook script sends it with every call,
    /// so other programs and web pages can't fake Claude Code requests.
    /// </summary>
    public static string TokenPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", "bridge.token");

    private static readonly Lazy<byte[]> Token = new(() =>
    {
        try
        {
            if (File.Exists(TokenPath))
            {
                var existing = File.ReadAllText(TokenPath).Trim();
                if (existing.Length >= 32) return Encoding.ASCII.GetBytes(existing);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(TokenPath)!);
            var fresh = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            File.WriteAllText(TokenPath, fresh);
            return Encoding.ASCII.GetBytes(fresh);
        }
        catch { return RandomNumberGenerator.GetBytes(32); }   // unusable token = bridge stays closed
    });

    /// <summary>Only the hook script may talk to us: no browsers, the right secret, small JSON bodies.</summary>
    private static bool IsTrusted(HttpListenerRequest r)
    {
        if (r.HttpMethod != "POST") return false;
        if (r.Headers["Origin"] != null || r.Headers["Referer"] != null) return false;          // a web page
        if (!(r.ContentType ?? "").StartsWith("application/json", StringComparison.OrdinalIgnoreCase)) return false;
        if (r.ContentLength64 is < 0 or > MaxBody) return false;
        if (!r.IsLocal) return false;
        var sent = Encoding.ASCII.GetBytes(r.Headers["X-Island-Token"] ?? "");
        return CryptographicOperations.FixedTimeEquals(sent, Token.Value);
    }

    public sealed class PermissionRequest
    {
        public required string Tool { get; init; }
        public required string Details { get; init; }
        public required string Project { get; init; }
        /// <summary>"Claude Code" or "Codex".</summary>
        public string Agent { get; init; } = "Claude Code";
        public TaskCompletionSource<string?> Decision { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed record Notice(string Kind, string Title, string Message, string Project, string Agent = "Claude Code");

    private HttpListener? _listener;

    /// <summary>Raised on a background thread.</summary>
    public event Action<PermissionRequest>? PermissionRequested;
    public event Action<Notice>? Notified;

    public bool IsRunning => _listener?.IsListening == true;

    public void Start()
    {
        try
        {
            _ = Token.Value;   // the hook script needs the secret on disk before the first call
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
        if (!IsTrusted(ctx.Request))
        {
            try { ctx.Response.StatusCode = 403; ctx.Response.Close(); } catch { }
            return;
        }
        try
        {
            using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            var json = JsonNode.Parse(body) as JsonObject ?? new JsonObject();
            var project = Path.GetFileName((json["cwd"]?.GetValue<string>() ?? "").TrimEnd('\\', '/'));
            var agent = ctx.Request.QueryString["src"] == "codex" ? "Codex" : "Claude Code";

            switch (ctx.Request.Url?.AbsolutePath)
            {
                case "/permission":
                {
                    // too many open questions at once: let Claude Code ask in the terminal instead
                    if (Interlocked.Increment(ref _pending) > MaxPending)
                    {
                        Interlocked.Decrement(ref _pending);
                        break;
                    }
                    try
                    {
                    var req = new PermissionRequest
                    {
                        Tool = json["tool_name"]?.GetValue<string>() ?? "?",
                        Details = Describe(json["tool_name"]?.GetValue<string>(), json["tool_input"]),
                        Project = project,
                        Agent = agent,
                    };
                    PermissionRequested?.Invoke(req);
                    var winner = await Task.WhenAny(req.Decision.Task, Task.Delay(TimeSpan.FromSeconds(110)));
                    var behavior = winner == req.Decision.Task ? req.Decision.Task.Result : null;
                    req.Decision.TrySetResult(null);

                    if (behavior is "allow" or "deny")
                    {
                        var decision = new JsonObject { ["behavior"] = behavior };
                        if (behavior == "deny") decision["message"] = "The user denied this from Atoll.";
                        response = new JsonObject
                        {
                            ["hookSpecificOutput"] = new JsonObject
                            {
                                ["hookEventName"] = "PermissionRequest",
                                ["decision"] = decision,
                            },
                        }.ToJsonString();
                    }
                    }
                    finally { Interlocked.Decrement(ref _pending); }
                    break; // empty response = ask in the terminal as usual
                }
                case "/notify":
                {
                    var evt = json["hook_event_name"]?.GetValue<string>() ?? "";
                    if (evt == "Stop")
                    {
                        var last = json["last_assistant_message"]?.GetValue<string>() ?? "";
                        Notified?.Invoke(new Notice("stop", agent == "Codex" ? L.T("Codex закончил") : L.T("Claude Code закончил"), last, project, agent));
                    }
                    else
                    {
                        var type = json["notification_type"]?.GetValue<string>() ?? "";
                        var msg = json["message"]?.GetValue<string>() ?? "";
                        Notified?.Invoke(new Notice(type, json["title"]?.GetValue<string>() ?? agent, msg, project, agent));
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
            "Bash" or "PowerShell" or "apply_patch" or "shell" => Get("command"),
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

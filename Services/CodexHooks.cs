using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DynamicIsland.Services;

/// <summary>
/// Adds / removes the island's hooks in ~/.codex/hooks.json (OpenAI Codex CLI). Codex hooks use the same
/// shape as Claude Code's: PermissionRequest (allow / deny from the island) and Stop ("Codex finished").
/// Only ever touches entries whose command mentions the island's hook script. Codex asks the user to
/// trust new hooks once (/hooks in Codex).
/// </summary>
public static class CodexHooks
{
    private static readonly string[] Events = ["PermissionRequest", "Stop"];
    private const string Marker = "claude-hook.ps1";

    private static string HooksPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "hooks.json");

    private static string ScriptPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", Marker);

    /// <summary>Codex is (or was) used on this computer.</summary>
    public static bool CodexFound => Directory.Exists(Path.GetDirectoryName(HooksPath));

    public static bool IsInstalled
    {
        get
        {
            try { return File.Exists(HooksPath) && File.ReadAllText(HooksPath).Contains(Marker); }
            catch { return false; }
        }
    }

    public static void Install()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ScriptPath)!);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", Marker), ScriptPath, overwrite: true);

        var root = Load();
        var hooks = root["hooks"] as JsonObject ?? new JsonObject();
        root["hooks"] = hooks;
        foreach (var evt in Events)
        {
            var list = hooks[evt] as JsonArray ?? new JsonArray();
            hooks[evt] = list;
            RemoveOurs(list);
            var command = $"powershell -NoProfile -ExecutionPolicy Bypass -File \"{ScriptPath}\" {evt} codex";
            list.Add(new JsonObject
            {
                ["hooks"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "command",
                        ["command"] = command,
                        ["commandWindows"] = command,
                        ["timeout"] = evt == "PermissionRequest" ? 120 : 10,
                        ["statusMessage"] = evt == "PermissionRequest" ? "Asking on the Atoll island" : null,
                    },
                },
            });
        }
        Save(root);
    }

    public static void Uninstall()
    {
        if (!File.Exists(HooksPath)) return;
        var root = Load();
        if (root["hooks"] is not JsonObject hooks) return;
        foreach (var evt in Events)
        {
            if (hooks[evt] is not JsonArray list) continue;
            RemoveOurs(list);
            if (list.Count == 0) hooks.Remove(evt);
        }
        if (hooks.Count == 0) root.Remove("hooks");
        Save(root);
    }

    private static void RemoveOurs(JsonArray list)
    {
        for (int i = list.Count - 1; i >= 0; i--)
            if (list[i]?.ToJsonString().Contains(Marker) == true)
                list.RemoveAt(i);
    }

    private static JsonObject Load()
    {
        if (!File.Exists(HooksPath)) return new JsonObject();
        var text = File.ReadAllText(HooksPath);
        if (string.IsNullOrWhiteSpace(text)) return new JsonObject();
        return JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        }) as JsonObject ?? throw new InvalidDataException(L.T("hooks.json не является JSON-объектом"));
    }

    private static void Save(JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HooksPath)!);
        var backup = HooksPath + ".before-atoll";
        if (File.Exists(HooksPath) && !File.Exists(backup)) File.Copy(HooksPath, backup);
        // drop the nulls (statusMessage on Stop)
        foreach (var evt in Events)
            if (root["hooks"]?[evt] is JsonArray l)
                foreach (var h in l.SelectMany(x => (x?["hooks"] as JsonArray) ?? []).OfType<JsonObject>())
                    foreach (var k in h.Where(kv => kv.Value == null).Select(kv => kv.Key).ToList()) h.Remove(k);
        File.WriteAllText(HooksPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}

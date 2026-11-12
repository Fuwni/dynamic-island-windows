using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DynamicIsland.Services;

/// <summary>
/// Adds / removes the island's hooks in ~/.claude/settings.json.
/// Only ever touches entries whose command mentions claude-hook.ps1.
/// </summary>
public static class ClaudeHooks
{
    private static readonly string[] Events = ["PermissionRequest", "Notification", "Stop"];
    private const string Marker = "claude-hook.ps1";

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    private static string ScriptPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIsland", Marker);

    public static bool IsInstalled
    {
        get
        {
            try
            {
                return File.Exists(SettingsPath) && File.ReadAllText(SettingsPath).Contains(Marker);
            }
            catch { return false; }
        }
    }

    /// <summary>Keep the installed hook script up to date with this version of the app.</summary>
    public static void RefreshScript()
    {
        try
        {
            if (IsInstalled) File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", Marker), ScriptPath, overwrite: true);
        }
        catch { }
    }

    public static void Install()
    {
        // stable copy of the script, independent of where the app is built
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
            list.Add(new JsonObject
            {
                ["hooks"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "command",
                        ["command"] = $"powershell -NoProfile -ExecutionPolicy Bypass -File \"{ScriptPath}\" {evt}",
                        ["timeout"] = evt == "PermissionRequest" ? 120 : 10,
                    },
                },
            });
        }
        Save(root);
    }

    public static void Uninstall()
    {
        if (!File.Exists(SettingsPath)) return;
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
        if (!File.Exists(SettingsPath)) return new JsonObject();
        var text = File.ReadAllText(SettingsPath);
        if (string.IsNullOrWhiteSpace(text)) return new JsonObject();
        return JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        }) as JsonObject ?? throw new InvalidDataException("settings.json не является JSON-объектом");
    }

    private static void Save(JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var backup = SettingsPath + ".before-dynamic-island";
        if (File.Exists(SettingsPath) && !File.Exists(backup)) File.Copy(SettingsPath, backup);
        File.WriteAllText(SettingsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}

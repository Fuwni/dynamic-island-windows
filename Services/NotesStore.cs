using System.IO;
using System.Text.Json;

namespace DynamicIsland.Services;

public sealed class Note
{
    public string Text { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.Now;
}

/// <summary>Quick notes, kept in %APPDATA%\DynamicIsland\notes.json.</summary>
public static class NotesStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "notes.json");

    public static List<Note> Notes { get; } = Load();

    private static List<Note> Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<List<Note>>(File.ReadAllText(FilePath)) ?? [];
        }
        catch { }
        return [];
    }

    public static void Save()
    {
        if (Settings.ReadOnly) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Notes, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}

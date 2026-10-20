using System.IO;
using System.Text.Json;
using ArenaStart;

namespace ArenaLauncher;

public sealed class LauncherSettings
{
    private readonly string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "maschine", "ArenaLauncher", "settings.json");

    public string? Load()
    {
        EnvironmentCopy.AssertAncestors(path);
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<SavedFolder>(EnvironmentCopy.ReadPrivate(path))?.Source;
    }

    public void Save(string source)
    {
        EnvironmentCopy.AssertAncestors(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path)) _ = EnvironmentCopy.ReadPrivate(path);
        File.WriteAllText(path, JsonSerializer.Serialize(new SavedFolder(source)));
    }

    private sealed record SavedFolder(string Source);
}

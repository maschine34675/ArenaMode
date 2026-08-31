using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace ArenaMode.Tests;

/// <summary>
/// Finds the repository's own data files, and the game database when it is here.
///
/// Located from THIS source file's compile-time path rather than from the test
/// assembly's location: the output folder moves with configuration and target
/// framework, and a test suite that silently stops finding its fixtures is worse
/// than one that fails.
/// </summary>
internal static class RepoFiles
{
    private static string SourceDirectory([CallerFilePath] string path = "")
        => Path.GetDirectoryName(path)!;

    public static string Root => Path.GetFullPath(Path.Combine(SourceDirectory(), ".."));

    public static string Kits => Path.Combine(Root, "ArenaMode.Server", "defaults", "kits.json");

    public static string Ladder => Path.Combine(Root, "ArenaMode.Server", "defaults", "ladder.json");

    /// <summary>
    /// The live item database, if this machine has the game installed.
    ///
    /// Null when it is absent, and the tests that need it skip rather than fail:
    /// the structural invariants below are the point of this suite and they hold
    /// anywhere, while "does this template exist" can only be asked where the
    /// game is. A CI box without Tarkov should still get a useful answer.
    /// </summary>
    public static string? ItemDatabase
    {
        get
        {
            var path = Environment.GetEnvironmentVariable("SPT_DATABASE")
                       ?? @"D:\SPT41\SPT_Runtime\SPT_Data\database";

            var items = Path.Combine(path, "templates", "items.json");
            return File.Exists(items) ? items : null;
        }
    }

    public static JsonArray LoadArray(string path)
    {
        Assert.True(File.Exists(path), $"missing fixture: {path}");

        var node = JsonNode.Parse(File.ReadAllText(path));
        var array = node as JsonArray;
        Assert.NotNull(array);
        return array!;
    }
}

using System;
using System.IO;

namespace ArenaMode;

/// <summary>Keep assets and user drafts stable across flat and grouped plugin installs.</summary>
internal static class ArenaAssetPaths
{
    internal const string DirectoryName = "maschine-ArenaMode";
    internal static string FromPluginDirectory(string pluginDirectory)
    {
        if (string.IsNullOrWhiteSpace(pluginDirectory))
            throw new ArgumentException("The plugin directory is required.", nameof(pluginDirectory));

        string directory = Path.GetFullPath(pluginDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string name = Path.GetFileName(directory);
        if (string.Equals(name, DirectoryName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "ArenaMode", StringComparison.OrdinalIgnoreCase))
            return directory;

        string canonical = Path.Combine(directory + Path.DirectorySeparatorChar, DirectoryName);
        string legacy = Path.Combine(directory + Path.DirectorySeparatorChar, "ArenaMode");
        // Old flat DLLs may still be tested before the first migrated deployment.
        // Prefer the new folder whenever it exists; never mix two asset trees.
        return !Directory.Exists(canonical) && Directory.Exists(legacy) ? legacy : canonical;
    }

    internal static string FromPluginFile(string pluginFile) =>
        FromPluginDirectory(Path.GetDirectoryName(pluginFile)
            ?? throw new ArgumentException("The plugin file must include its directory.", nameof(pluginFile)));
}

using System;
using System.IO;

namespace ArenaMode;

/// <summary>Keep assets and user drafts stable across flat and grouped plugin installs.</summary>
internal static class ArenaAssetPaths
{
    internal static string FromPluginDirectory(string pluginDirectory)
    {
        if (string.IsNullOrWhiteSpace(pluginDirectory))
            throw new ArgumentException("The plugin directory is required.", nameof(pluginDirectory));

        string directory = Path.GetFullPath(pluginDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(Path.GetFileName(directory), "ArenaMode", StringComparison.OrdinalIgnoreCase)
            ? directory
            : Path.Combine(directory + Path.DirectorySeparatorChar, "ArenaMode");
    }

    internal static string FromPluginFile(string pluginFile) =>
        FromPluginDirectory(Path.GetDirectoryName(pluginFile)
            ?? throw new ArgumentException("The plugin file must include its directory.", nameof(pluginFile)));
}

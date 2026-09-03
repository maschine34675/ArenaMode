namespace ArenaMode.Bots;

internal enum TeamArenaSainAssemblyState
{
    ConfirmedAbsent,
    InstalledPending,
    Loaded
}

/// <summary>
/// Distinguishes an optional SAIN installation from a plugin whose assembly is
/// merely not available yet. Only confirmed absence permits Arena to proceed
/// without suspending or refreshing SAIN state.
/// </summary>
internal static class TeamArenaSainAssemblyPolicy
{
    internal static TeamArenaSainAssemblyState Select(
        bool pluginMetadataPresent,
        bool assemblyLoaded)
    {
        if (assemblyLoaded)
            return TeamArenaSainAssemblyState.Loaded;

        return pluginMetadataPresent
            ? TeamArenaSainAssemblyState.InstalledPending
            : TeamArenaSainAssemblyState.ConfirmedAbsent;
    }
}

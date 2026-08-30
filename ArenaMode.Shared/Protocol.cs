namespace ArenaMode.Shared;

/// <summary>
/// Route constants shared between ArenaMode.Client and ArenaMode.Server
/// (linked source file, compiled into both assemblies).
/// </summary>
public static class Protocol
{
    public const string PingRoute = "/arenamode/ping";
    public const string ConfigRoute = "/arenamode/config";
    public const string KitsRoute = "/arenamode/kits";
    public const string LadderRoute = "/arenamode/ladder";

    /// <summary>Cut a fresh ladder mid-raid, for when the player finishes the one they had.</summary>
    public const string LadderRedrawRoute = "/arenamode/ladder/redraw";
    public const string RaidStartRoute = "/arenamode/raid-start";
    public const string RaidCancelRoute = "/arenamode/raid-cancel";
    public const string RaidEndRoute = "/arenamode/raid-end";
    public const string StatusRoute = "/arenamode/status";

    /// <summary>The client uploads its own BepInEx settings here so the config page can show them.</summary>
    public const string ClientSettingsPushRoute = "/arenamode/client-settings/push";

    /// <summary>The client asks here whether the page changed anything since it last looked.</summary>
    public const string ClientSettingsRoute = "/arenamode/client-settings";
}

using ArenaMode.Server.Services;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services.Bot;

namespace ArenaMode.Server;

/// <summary>
/// Static bridge so Harmony patch methods (which are static) can reach the
/// DI-managed services. Populated by PatchManager at load; same pattern as
/// Redeploy's static helpers.
/// </summary>
public static class ArenaContext
{
    public static ArenaSessionService? Sessions { get; internal set; }
    public static ProfileSnapshotService? Snapshots { get; internal set; }
    public static ArenaProtectedRaidStartService? ProtectedRaidStart { get; internal set; }
    public static ArenaProtectedRaidEndService? ProtectedRaidEnd { get; internal set; }
    public static OrdinaryRaidLifecycleService? OrdinaryRaids { get; internal set; }
    public static MatchBotDetailsCacheService? MatchBotCache { get; internal set; }
    public static ISptLogger<PatchManager>? Logger { get; internal set; }
    public static ArenaModeConfig Config { get; internal set; } = new();

    /// <summary>Raw kit catalog JSON served verbatim to the client (config/kits.json).</summary>
    public static string KitsJson { get; internal set; } = "[]";

    /// <summary>
    /// Gun Game weapon ladder, same shape as a kit but carrying only a primary
    /// weapon (config/ladder.json). Generated from the game's own weapon presets
    /// by tools/generate-ladder.py and shipped as data, so it stays editable.
    /// </summary>
    public static string LadderJson { get; internal set; } = "[]";
}

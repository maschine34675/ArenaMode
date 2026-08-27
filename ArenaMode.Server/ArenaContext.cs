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
    public static MatchBotDetailsCacheService? MatchBotCache { get; internal set; }
    public static ISptLogger<PatchManager>? Logger { get; internal set; }
    public static ArenaModeConfig Config { get; internal set; } = new();
}

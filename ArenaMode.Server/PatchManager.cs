using ArenaMode.Server.Patches;
using ArenaMode.Server.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services.Bot;

namespace ArenaMode.Server;

// Preload+60: after Redeploy's patch manager (Preload+50) purely for deterministic
// log ordering — the patch sets are independent.
[Injectable(TypePriority = OnLoadOrder.Preload + 60)]
public sealed class PatchManager(
    ArenaSessionService sessions,
    ProfileSnapshotService snapshots,
    MatchBotDetailsCacheService matchBotCache,
    ISptLogger<PatchManager> logger) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        ArenaContext.Sessions = sessions;
        ArenaContext.Snapshots = snapshots;
        ArenaContext.MatchBotCache = matchBotCache;
        ArenaContext.Logger = logger;

        new StartLocalRaidPatch().Enable();
        new PostRaidPmcSkipPatch().Enable();
        new PreRaidWipeSkipPatch().Enable();
        new GenerateLocationAndLootPatch().Enable();
        new AdjustBotHostilityPatch().Enable();
        new BotCapPatch().Enable();

        logger.Info("[ArenaMode] Server patches enabled.");
        return Task.CompletedTask;
    }
}

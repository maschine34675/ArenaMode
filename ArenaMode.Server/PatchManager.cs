using ArenaMode.Server.Patches;
using ArenaMode.Server.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services.Bot;
using SPTarkov.Reflection.Patching;

namespace ArenaMode.Server;

// Preload+60: after Redeploy's patch manager (Preload+50) purely for deterministic
// log ordering — the patch sets are independent.
[Injectable(TypePriority = OnLoadOrder.Preload + 60)]
public sealed class PatchManager(
    ArenaSessionService sessions,
    ProfileSnapshotService snapshots,
    ArenaProtectedRaidStartService protectedRaidStart,
    ArenaProtectedRaidEndService protectedRaidEnd,
    OrdinaryRaidLifecycleService ordinaryRaids,
    MatchBotDetailsCacheService matchBotCache,
    ISptLogger<PatchManager> logger) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        ArenaContext.Sessions = sessions;
        ArenaContext.Snapshots = snapshots;
        ArenaContext.ProtectedRaidStart = protectedRaidStart;
        ArenaContext.ProtectedRaidEnd = protectedRaidEnd;
        ArenaContext.OrdinaryRaids = ordinaryRaids;
        ArenaContext.MatchBotCache = matchBotCache;
        ArenaContext.Logger = logger;

        new StartLocalRaidPatch().Enable();
        new EndLocalRaidPatch().Enable();
        new PostRaidPmcSkipPatch().Enable();
        new PreRaidWipeSkipPatch().Enable();
        new GenerateLocationAndLootPatch().Enable();
        new AdjustBotHostilityPatch().Enable();
        new BotCapPatch().Enable();
        new BotTierScalePatch().Enable();
        new TeamDeathmatchArmbandPatch().Enable();
        AbstractPatch[] pitFireTeamFilePatches =
        [
            new PitFireTeamTextWritePatch(),
            new PitFireTeamByteWritePatch(),
            new PitFireTeamAsyncTextWritePatch(),
            new PitFireTeamAsyncByteWritePatch(),
            new PitFireTeamDeleteFilePatch(),
        ];
        bool pitFireTeamFilesReady = true;
        string? pitFireTeamFileFailure = null;
        try
        {
            foreach (AbstractPatch patch in pitFireTeamFilePatches)
                patch.Enable();
        }
        catch (Exception ex)
        {
            pitFireTeamFilesReady = false;
            pitFireTeamFileFailure = $"SPT FileUtil persistence patch failed: {ex.Message}";

            // A partial file boundary is not a protection contract. Roll back
            // every prefix that was enabled before the failure and advertise a
            // stable not-ready state for the raid-start router.
            foreach (AbstractPatch patch in pitFireTeamFilePatches.Reverse())
            {
                if (!patch.IsActive)
                    continue;

                try
                {
                    patch.Disable();
                }
                catch (Exception rollbackError)
                {
                    logger.Error(
                        $"[ArenaMode] Failed to roll back partial PitFireTeam FileUtil patch " +
                        $"{patch.GetType().Name}: {rollbackError}");
                }
            }
        }

        PitFireTeamDynamicPersistencePatches.TryEnable(
            pitFireTeamFilesReady,
            pitFireTeamFileFailure);

        logger.Info("[ArenaMode] Server patches enabled.");
        return Task.CompletedTask;
    }
}

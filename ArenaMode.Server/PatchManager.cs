using ArenaMode.Server.Patches;
using ArenaMode.Server.Compat;
using ArenaMode.Server.Services;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services.Bot;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Spt.Mod;

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
    SPTarkov.Server.Core.Models.Spt.Tables.LocationTable locationTable,
    IReadOnlyList<SptMod> loadedMods,
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
        // Before ArenaStartLocalRaidPatch is enabled below: its refusal of
        // non-Arena starts on the Arena session server reads this. A failed probe
        // must never keep a normal server from starting; it stays inert instead.
        try
        {
            ArenaServerInstance.Initialize(loadedMods);
        }
        catch (Exception ex)
        {
            logger.Warning(
                $"[ArenaMode] Arena session probe failed; this server is treated as a normal one: {ex.Message}");
        }

        ArenaSettingsRequestLogPatch.TryEnable();
        ArenaOptionalMapPolicy.FindLocation = id => locationTable.GetDictionary()
            .TryGetValue(id, out var location) ? location.Base : null;

        new ArenaStartLocalRaidPatch().Enable();
        new ArenaEndLocalRaidPatch().Enable();
        ArenaSvmCompat.TryEnable(loadedMods);
        new ArenaPostRaidPmcSkipPatch().Enable();
        new ArenaPreRaidWipeSkipPatch().Enable();
        new ArenaGenerateLocationAndLootPatch().Enable();
        new ArenaAdjustBotHostilityPatch().Enable();
        new ArenaServerBotCapPatch().Enable();
        new ArenaBotTierScalePatch().Enable();
        ApbsBotTierCompatibility.TryEnable(loadedMods);
        new ArenaTeamDeathmatchArmbandPatch().Enable();
        AbstractPatch[] pitFireTeamFilePatches =
        [
            new ArenaPitFireTeamTextWritePatch(),
            new ArenaPitFireTeamByteWritePatch(),
            new ArenaPitFireTeamAsyncTextWritePatch(),
            new ArenaPitFireTeamAsyncByteWritePatch(),
            new ArenaPitFireTeamDeleteFilePatch(),
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
        ArenaServerInstance.LogStartup(logger);
        return Task.CompletedTask;
    }
}

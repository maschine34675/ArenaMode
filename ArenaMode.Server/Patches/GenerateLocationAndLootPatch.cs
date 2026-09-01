using ArenaMode.Server.Services;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Services.InRaid;
using System.Reflection;

namespace ArenaMode.Server.Patches;

/// <summary>
/// GenerateLocationAndLoot works on a PER-RAID clone of the location — the single
/// safe hook for all arena map mutations (waves, caps, timers, loot, ABPS pump
/// fields). Runs inside StartLocalRaidAsync AFTER our arm-consumption prefix, so
/// the session is already InRaid for an arena raid.
/// </summary>
public sealed class GenerateLocationAndLootPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(LocationLifecycleService).GetMethod(
            nameof(LocationLifecycleService.GenerateLocationAndLoot),
            BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPostfix]
    public static void Postfix(MongoId sessionId, string name, LocationBase __result)
    {
        var sessions = ArenaContext.Sessions;
        if (sessions == null || !sessions.IsInRaid(sessionId))
            return;

        var protectedTeam = sessions.IsProtectedTeamRaid(sessionId);
        if (__result == null)
        {
            if (protectedTeam)
                sessions.MarkLocationMutationFailed(sessionId, "the server returned no location clone");
            return;
        }

        if (string.IsNullOrWhiteSpace(name)
            || !ArenaContext.Config.Maps.TryGetValue(name.ToLowerInvariant(), out var mapConfig)
            || !mapConfig.Enabled)
        {
            if (protectedTeam)
                sessions.MarkLocationMutationFailed(sessionId, $"Arena map '{name}' is no longer enabled");
            return;
        }

        try
        {
            var mode = sessions.GetStatus(sessionId).mode;
            ArenaLocationMutator.Mutate(__result, mapConfig, ArenaContext.Config, mode);
            sessions.StashLocationClone(sessionId, __result);
            sessions.MarkLocationMutationReady(sessionId);

            var strategy = ArenaLocationMutator.IsQuietMap(mode)
                ? "silent (the mode spawns its own squads)"
                : ArenaLocationMutator.AbpsPresent ? "ABPS pump" : "generated waves";

            ArenaContext.Logger?.Info(
                $"[ArenaMode] Location clone mutated for arena ({name}, mode={mode ?? "?"}): botMax={mapConfig.BotMax}, " +
                $"spawn strategy={strategy}, " +
                $"raid={ArenaContext.Config.RaidTimeMinutes}min, stripLoot={ArenaContext.Config.StripLoot}.");
        }
        catch (Exception ex)
        {
            if (protectedTeam)
            {
                sessions.MarkLocationMutationFailed(sessionId, ex.GetBaseException().Message);
                ArenaContext.Logger?.Error(
                    $"[ArenaMode] Protected 5v5 location mutation failed — client must end the raid as no contest: {ex}");
                return;
            }

            ArenaContext.Logger?.Error(
                $"[ArenaMode] Location mutation failed — solo Arena raid runs with vanilla spawns: {ex}");
        }
    }
}

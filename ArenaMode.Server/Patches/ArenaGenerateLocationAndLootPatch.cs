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
public sealed class ArenaGenerateLocationAndLootPatch : AbstractPatch
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
        var areaId = sessions.GetAreaId(sessionId);
        var areaTeamSize = sessions.GetAreaTeamSize(sessionId);
        var participants = sessions.GetParticipants(sessionId);
        var boundedArea = areaId != ArenaMode.Shared.ArenaAreaCatalog.FullMapId;
        var requiresLocation = protectedTeam || boundedArea || participants.HasValue;
        if (__result == null)
        {
            if (requiresLocation)
                sessions.MarkLocationMutationFailed(sessionId, "the server returned no location clone");
            return;
        }

        // Native start is already bound to one map. A mismatched callback must
        // not mutate or stash a different clone under that participant contract.
        if (participants.HasValue
            && (!string.Equals(sessions.GetStatus(sessionId).map, name, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(__result.Id, name, StringComparison.OrdinalIgnoreCase)))
        {
            sessions.MarkLocationMutationFailed(sessionId, "the participant-managed location did not match its exact armed map");
            return;
        }

        if (string.IsNullOrWhiteSpace(name)
            || !ArenaContext.Config.Maps.TryGetValue(name.ToLowerInvariant(), out var mapConfig)
            || !mapConfig.Enabled)
        {
            if (requiresLocation)
                sessions.MarkLocationMutationFailed(sessionId, $"Arena map '{name}' is no longer enabled");
            return;
        }

        try
        {
            var mode = sessions.GetStatus(sessionId).mode;
            ArenaLocationMutator.Mutate(__result, mapConfig, ArenaContext.Config, mode, areaId, areaTeamSize, participants);
            sessions.StashLocationClone(sessionId, __result);
            sessions.MarkLocationMutationReady(sessionId);

            var strategy = ArenaLocationMutator.IsQuietMap(mode) || participants.HasValue
                ? "silent (the mode spawns its own squads)"
                : ArenaLocationMutator.AbpsPresent ? "ABPS pump" : "generated waves";

            ArenaContext.Logger?.Debug(
                $"[ArenaMode] Location clone mutated for arena ({name}, mode={mode ?? "?"}): botMax={__result.BotMax}, " +
                $"spawn strategy={strategy}, area={areaId}, areaTeamSize={areaTeamSize}, participants={participants}, " +
                $"raid={ArenaContext.Config.RaidTimeMinutes}min, stripLoot={ArenaContext.Config.StripLoot}.");
        }
        catch (Exception ex)
        {
            if (requiresLocation)
            {
                sessions.MarkLocationMutationFailed(sessionId, ex.GetBaseException().Message);
                ArenaContext.Logger?.Error(
                    $"[ArenaMode] Required Arena location mutation failed (area={areaId}, protectedTeam={protectedTeam}); "
                    + $"the invalid raid must not become playable: {ex}");
                return;
            }

            ArenaContext.Logger?.Error(
                $"[ArenaMode] Location mutation failed — solo Arena raid runs with vanilla spawns: {ex}");
        }
    }
}

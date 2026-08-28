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
        if (sessions == null || __result == null || !sessions.IsInRaid(sessionId))
            return;

        if (!ArenaContext.Config.Maps.TryGetValue(name.ToLowerInvariant(), out var mapConfig) || !mapConfig.Enabled)
            return;

        try
        {
            ArenaLocationMutator.Mutate(__result, mapConfig, ArenaContext.Config);
            sessions.StashLocationClone(sessionId, __result);
            ArenaContext.Logger?.Info(
                $"[ArenaMode] Location clone mutated for arena ({name}): botMax={mapConfig.BotMax}, " +
                $"spawn strategy={(ArenaLocationMutator.AbpsPresent ? "ABPS pump" : "generated waves")}, " +
                $"raid={ArenaContext.Config.RaidTimeMinutes}min, stripLoot={ArenaContext.Config.StripLoot}.");
        }
        catch (Exception ex)
        {
            ArenaContext.Logger?.Error($"[ArenaMode] Location mutation failed — raid runs with vanilla spawns: {ex}");
        }
    }
}

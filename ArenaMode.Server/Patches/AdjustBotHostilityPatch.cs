using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Services.InRaid;
using System.Reflection;

namespace ArenaMode.Server.Patches;

/// <summary>
/// AdjustBotHostilitySettings runs AFTER GenerateLocationAndLoot inside
/// StartLocalRaidAsync and rewrites the clone's hostility from pmcConfig — this
/// postfix runs after THAT, so arena hostility always wins. Reference-equality
/// against the stashed clone guarantees normal raids are never touched.
/// </summary>
public sealed class AdjustBotHostilityPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(LocationLifecycleService).GetMethod(
            "AdjustBotHostilitySettings",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
    }

    [PatchPostfix]
    public static void Postfix(LocationBase location)
    {
        var sessions = ArenaContext.Sessions;
        if (sessions == null || location == null || !sessions.IsStashedClone(location))
            return;

        var settings = location.BotLocationModifier?.AdditionalHostilitySettings;
        if (settings == null)
            return;

        var freeForAll = !string.Equals(ArenaContext.Config.HostilityMode, "all_vs_player", StringComparison.OrdinalIgnoreCase);

        foreach (var entry in settings)
        {
            // Every role always hates the player's PMC side.
            entry.BearEnemyChance = 100;
            entry.UsecEnemyChance = 100;

            if (freeForAll)
            {
                entry.SavageEnemyChance = 100;
                entry.SavagePlayerBehaviour = "AlwaysEnemies";
                entry.AlwaysFriends?.Clear();

                if (entry.ChancedEnemies != null)
                {
                    foreach (var chanced in entry.ChancedEnemies)
                        chanced.EnemyChance = 100;
                }
            }
        }

        ArenaContext.Logger?.Info(
            $"[ArenaMode] Arena hostility applied ({(freeForAll ? "ffa" : "all_vs_player")}).");
    }
}

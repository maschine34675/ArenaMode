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
        if (sessions == null || location == null
            || !sessions.TryGetStashedCloneMode(location, out var mode))
            return;

        if (string.Equals(mode, "teamdeathmatch", StringComparison.OrdinalIgnoreCase))
        {
            ApplyTeamDeathmatch(location);
            ArenaContext.Logger?.Info("[ArenaMode] Arena hostility applied (USEC vs BEAR teams).");
            return;
        }

        var settings = location.BotLocationModifier?.AdditionalHostilitySettings;
        if (settings == null)
            return;

        var freeForAll = !string.Equals(ArenaContext.Config.HostilityMode, "all_vs_player", StringComparison.OrdinalIgnoreCase);

        foreach (var entry in settings)
        {
            // Every role always hates the player's PMC side — and unconditionally:
            // the *EnemyChance values are only consulted under the ChancedEnemies
            // behaviour, so force the behaviour itself.
            entry.BearEnemyChance = 100;
            entry.UsecEnemyChance = 100;
            entry.BearPlayerBehaviour = "AlwaysEnemies";
            entry.UsecPlayerBehaviour = "AlwaysEnemies";

            if (freeForAll)
            {
                entry.SavageEnemyChance = 100;
                entry.SavagePlayerBehaviour = "AlwaysEnemies";
                entry.AlwaysFriends?.Clear();

                // COPY-ON-WRITE, never mutate in place: SPT's own hostility rewrite
                // fills this list with the pmcConfig SINGLETON's ChancedEnemy records
                // (added by reference) — writing into them would leak EnemyChance=100
                // into every subsequent NORMAL raid for the rest of the server session.
                if (entry.ChancedEnemies != null)
                {
                    entry.ChancedEnemies = entry.ChancedEnemies
                        .Select(chanced => chanced with { EnemyChance = 100 })
                        .ToList();
                }
            }
        }

        ArenaContext.Logger?.Info(
            $"[ArenaMode] Arena hostility applied ({(freeForAll ? "ffa" : "all_vs_player")}).");
    }

    private static void ApplyTeamDeathmatch(LocationBase location)
    {
        var modifier = location.BotLocationModifier;
        if (modifier == null)
            return;

        // SPT's rewrite can add singleton pmcConfig ChancedEnemy records by
        // reference. Replace each PMC record and every edited child collection
        // instead of changing that shared data. Unrelated bot roles stay intact.
        var entries = (modifier.AdditionalHostilitySettings ?? []).ToList();
        foreach (var role in new[] { "pmcUSEC", "pmcBEAR" })
        {
            var found = false;
            for (var index = 0; index < entries.Count; index++)
            {
                if (!string.Equals(entries[index].BotRole, role, StringComparison.OrdinalIgnoreCase))
                    continue;
                entries[index] = TeamEntry(entries[index], role);
                found = true;
            }

            // Some map/config combinations omit a PMC role. Native BotSettings
            // falls back to cached difficulty settings when its role is absent.
            if (!found)
                entries.Add(TeamEntry(new AdditionalHostilitySettings
                {
                    SavageEnemyChance = 100,
                    SavagePlayerBehaviour = "AlwaysEnemies",
                }, role));
        }

        modifier.AdditionalHostilitySettings = entries;
    }

    private static AdditionalHostilitySettings TeamEntry(AdditionalHostilitySettings source, string role)
    {
        var usec = string.Equals(role, "pmcUSEC", StringComparison.Ordinal);
        var enemyRole = usec ? "pmcBEAR" : "pmcUSEC";
        var friends = WithoutPmcRoles(source.AlwaysFriends).ToHashSet(StringComparer.OrdinalIgnoreCase);
        friends.Add(role);
        var enemies = WithoutPmcRoles(source.AlwaysEnemies).ToHashSet(StringComparer.OrdinalIgnoreCase);
        enemies.Add(enemyRole);

        // BotSettings.GetEnemyBotTypes removes Neutral/Warn/ChancedEnemies AFTER
        // adding AlwaysEnemies; keeping either PMC role in those arrays would
        // override the deterministic faction relation. Human PMC behavior has
        // its own AlwaysFriends/AlwaysEnemies switch, independent of bot roles.
        return source with
        {
            BotRole = role,
            AlwaysFriends = friends,
            AlwaysEnemies = enemies,
            Neutral = WithoutPmcRoles(source.Neutral).ToHashSet(StringComparer.OrdinalIgnoreCase),
            Warn = WithoutPmcRoles(source.Warn).ToArray(),
            ChancedEnemies = (source.ChancedEnemies ?? [])
                .Where(chanced => !IsPmcRole(chanced.Role))
                .Select(chanced => chanced with { })
                .ToList(),
            UsecEnemyChance = usec ? 0 : 100,
            BearEnemyChance = usec ? 100 : 0,
            UsecPlayerBehaviour = usec ? "AlwaysFriends" : "AlwaysEnemies",
            BearPlayerBehaviour = usec ? "AlwaysEnemies" : "AlwaysFriends",
        };
    }

    private static IEnumerable<string> WithoutPmcRoles(IEnumerable<string>? roles)
        => (roles ?? []).Where(role => !IsPmcRole(role));

    private static bool IsPmcRole(string? role)
        => string.Equals(role, "pmcUSEC", StringComparison.OrdinalIgnoreCase)
           || string.Equals(role, "pmcBEAR", StringComparison.OrdinalIgnoreCase);
}

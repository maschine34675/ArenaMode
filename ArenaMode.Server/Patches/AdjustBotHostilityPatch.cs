using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Services.InRaid;
using System.Reflection;
using ArenaMode.Shared;

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
            || !sessions.TryGetStashedCloneMode(location, out var mode, out var deathmatchHostility))
            return;

        if (DeathmatchSelection.IsDeathmatch(mode))
        {
            // The arm constructor rejects missing/unknown values. Do not fall
            // back to a mutable server default if that invariant ever drifts.
            if (!DeathmatchSelection.TryResolve(mode, deathmatchHostility, out _, out var reason))
                throw new InvalidOperationException(reason);
            ApplyDeathmatch(location, deathmatchHostility == DeathmatchSelection.FreeForAll);
            ArenaContext.Logger?.Info($"[ArenaMode] Arena hostility applied ({deathmatchHostility}, frozen Deathmatch selection).");
            return;
        }

        if (string.Equals(mode, "teamdeathmatch", StringComparison.OrdinalIgnoreCase))
        {
            ApplyTeamDeathmatch(location);
            ArenaContext.Logger?.Info("[ArenaMode] Arena hostility applied (USEC vs BEAR teams).");
            return;
        }

        if (string.Equals(mode, ArenaPopulationPolicy.Zombie, StringComparison.OrdinalIgnoreCase))
        {
            // Deliberately untouched. Verified against the installed server
            // database (SPT_Data/database/bots/types/infectedassault.json,
            // difficulty.normal.Mind — the other three generic infected types
            // match): DEFAULT_BEAR_BEHAVIOUR, DEFAULT_USEC_BEHAVIOUR AND
            // DEFAULT_SAVAGE_BEHAVIOUR are all "AlwaysEnemies" already, and
            // FRIENDLY_BOT_TYPES lists every infected role (including
            // infectedTagilla) as friendly to the others. That is exactly the
            // relation v0 wants — hostile to every player side, friendly to
            // one another — with no AdditionalHostilitySettings entry at all.
            // Falling into the generic branch below would apply whatever
            // ArenaContext.Config.HostilityMode says instead: "ffa" in
            // particular sets SavageEnemyChance to 100 and
            // SavagePlayerBehaviour to AlwaysEnemies for every Savage-side
            // entry, which would make every zombie (side Savage, per the
            // infected*.json profiles) an enemy of every OTHER zombie too —
            // the one relation this mode must never produce. Returning here,
            // before that branch runs, is what keeps the clone's hostility
            // settings vanilla regardless of the server's configured mode.
            ArenaContext.Logger?.Info("[ArenaMode] Arena hostility left vanilla (zombie: native infected Mind settings already apply).");
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

    private static void ApplyDeathmatch(LocationBase location, bool freeForAll)
    {
        var modifier = location.BotLocationModifier;
        if (modifier == null)
            return;

        // The four ordinary Arena spawn roles must have a record even when a
        // map omits it. Preserve the remaining map roles and use their complete
        // relation vocabulary, including bosses when enabled in Arena config.
        var entries = (modifier.AdditionalHostilitySettings ?? []).ToList();
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "pmcUSEC", "pmcBEAR", "assault", "marksman" };
        foreach (var entry in entries)
        {
            if (!string.IsNullOrEmpty(entry.BotRole)) roles.Add(entry.BotRole);
            roles.UnionWith(entry.AlwaysFriends ?? []);
            roles.UnionWith(entry.AlwaysEnemies ?? []);
            roles.UnionWith(entry.Neutral ?? []);
            roles.UnionWith(entry.Warn ?? []);
            roles.UnionWith((entry.ChancedEnemies ?? []).Select(enemy => enemy.Role));
        }
        roles.RemoveWhere(string.IsNullOrWhiteSpace);

        foreach (string role in new[] { "pmcUSEC", "pmcBEAR", "assault", "marksman" })
            if (!entries.Any(entry => string.Equals(entry.BotRole, role, StringComparison.OrdinalIgnoreCase)))
                entries.Add(new AdditionalHostilitySettings { BotRole = role });

        // BotSettings.GetEnemyBotTypes removes friends/neutral/warn/chanced
        // roles after adding enemies. Clear the conflicting lists instead of
        // merely changing chance percentages. Human PMC behavior is separate,
        // so an all-vs-player bot can befriend PMC bots while attacking the PMC.
        // Replace records AND collections: native SPT can insert pmcConfig
        // singleton records here by reference; ordinary raids must stay intact.
        modifier.AdditionalHostilitySettings = entries.Select(source => source with
        {
            AlwaysFriends = freeForAll ? [] : new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase),
            AlwaysEnemies = freeForAll ? new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase) : [],
            Neutral = [],
            Warn = [],
            ChancedEnemies = [],
            UsecPlayerBehaviour = "AlwaysEnemies",
            BearPlayerBehaviour = "AlwaysEnemies",
            SavagePlayerBehaviour = "AlwaysEnemies",
            UsecEnemyChance = 100,
            BearEnemyChance = 100,
            SavageEnemyChance = 100,
        }).ToList();
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

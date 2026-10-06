using ArenaMode.Shared;
using System;
using System.Collections.Generic;

namespace ArenaMode.Modes;

internal sealed class ArenaModeDefinition
{
    public ArenaModeDefinition(string id, string label, string description,
        string rules, string respawn, string equipment)
    {
        Id = id;
        Label = label;
        Description = description;
        Rules = rules;
        Respawn = respawn;
        Equipment = equipment;
    }

    public string Id { get; }
    public string Label { get; }
    public string Description { get; }
    public string Rules { get; }
    public string Respawn { get; }
    public string Equipment { get; }
}

/// <summary>Stable mode ids and the copy shown by the native map-screen picker.</summary>
internal static class ArenaModeCatalog
{
    public const string DeathmatchId = DeathmatchSelection.ModeId;
    public const string EndlessId = "endless";
    public const string RoundsId = "rounds";
    // From the preload plan, for the same reason the two below come from the
    // population policy: the loading-screen preload warms the first ladder
    // rung for this id, and a rename that did not reach the plan would warm
    // the wrong kit without any error.
    public const string GunGameId = Kits.KitPreloadPlan.GunGameModeId;
    // Taken from the shared policy rather than repeated: these two ids decide
    // whether the server silences the map's own population and whether the
    // client skips the profile preload, and a rename here that did not reach
    // ArenaPopulationPolicy would compile and quietly turn both off.
    public const string TacticalId = ArenaPopulationPolicy.Tactical;
    public const string TeamDeathmatchId = ArenaPopulationPolicy.TeamDeathmatch;
    // Same reasoning as TacticalId/TeamDeathmatchId above: taken from the
    // shared population policy, which this id also controls (Zombie owns its
    // entire population and turns on the native infection event), so a rename
    // here that did not reach ArenaPopulationPolicy would compile and quietly
    // stop closing the server's quiet clone for it.
    public const string ZombieId = ArenaPopulationPolicy.Zombie;

    private static readonly IReadOnlyList<ArenaModeDefinition> Definitions =
        Array.AsReadOnly(new[]
        {
            new ArenaModeDefinition(DeathmatchId, "DEATHMATCH",
                "Free-for-all or all against you, with optional rounds and kill/time limits.",
                "Individual kills", "Instant", "Arena kits"),
            new ArenaModeDefinition(GunGameId, "GUN GAME",
                "Advance through the weapon ladder with every kill.",
                "Weapon progression", "Instant", "Weapon ladder"),
            new ArenaModeDefinition(TacticalId, "TACTICAL",
                "Solo 1v5, or 5v5 with four PitFireTeam companions.",
                "Elimination rounds", "Next round", "Buy / retain kit"),
            new ArenaModeDefinition(TeamDeathmatchId, "TEAM DM",
                "USEC versus BEAR teams with instant respawns. No companions required.",
                "USEC vs BEAR", "Instant", "Arena kits"),
            new ArenaModeDefinition(ZombieId, "ZOMBIES",
                "Endless waves of the infected.",
                "Survive wave after wave; they only get bigger",
                "Instant respawn, the wave keeps coming", "Arena kits"),
        });

    internal const float ModePanelTop = 168f;
    // +42 for the fifth mode row (Zombie) added below the existing four; every
    // mode panel grows by one row height, not only the one with the most rows
    // of its own rules sub-view (Deathmatch).
    internal static float ModePanelHeight(string mode) => NormalizeId(mode) == DeathmatchId ? 596f : 530f;

    public static IReadOnlyList<ArenaModeDefinition> All => Definitions;

    public static string NormalizeId(string id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            var candidate = id.Trim();
            if (string.Equals(candidate, EndlessId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate, RoundsId, StringComparison.OrdinalIgnoreCase)) return DeathmatchId;
            foreach (var definition in Definitions)
            {
                if (string.Equals(definition.Id, candidate, StringComparison.OrdinalIgnoreCase))
                    return definition.Id;
            }
        }

        return DeathmatchId;
    }

    public static string ResolveSelection(string id, bool tacticalOnly)
        => tacticalOnly ? TacticalId : NormalizeId(id);

    public static bool CanSelect(string id, bool tacticalOnly)
        => !tacticalOnly
           || string.Equals(NormalizeId(id), TacticalId, StringComparison.Ordinal);
}

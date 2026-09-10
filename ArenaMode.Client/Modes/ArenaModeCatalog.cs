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
    public const string EndlessId = "endless";
    public const string RoundsId = "rounds";
    public const string GunGameId = "gungame";
    // Taken from the shared policy rather than repeated: these two ids decide
    // whether the server silences the map's own population and whether the
    // client skips the profile preload, and a rename here that did not reach
    // ArenaPopulationPolicy would compile and quietly turn both off.
    public const string TacticalId = ArenaPopulationPolicy.Tactical;
    public const string TeamDeathmatchId = ArenaPopulationPolicy.TeamDeathmatch;

    private static readonly IReadOnlyList<ArenaModeDefinition> Definitions =
        Array.AsReadOnly(new[]
        {
            new ArenaModeDefinition(EndlessId, "ENDLESS",
                "Continuous deathmatch with instant respawns.",
                "Deathmatch", "Instant", "Arena kits"),
            new ArenaModeDefinition(RoundsId, "ROUNDS",
                "Rounds with a kill target or time limit, and a break between them.",
                "Kill target / time limit", "Instant", "Arena kits"),
            new ArenaModeDefinition(GunGameId, "GUN GAME",
                "Advance through the weapon ladder with every kill.",
                "Weapon progression", "Instant", "Weapon ladder"),
            new ArenaModeDefinition(TacticalId, "TACTICAL",
                "Single-life rounds with bought or retained Arena kits.",
                "Elimination rounds", "Next round", "Buy / retain kit"),
            new ArenaModeDefinition(TeamDeathmatchId, "TEAM DM",
                "USEC versus BEAR teams with instant respawns. No companions required.",
                "USEC vs BEAR", "Instant", "Arena kits"),
        });

    public static IReadOnlyList<ArenaModeDefinition> All => Definitions;

    public static string NormalizeId(string id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            var candidate = id.Trim();
            foreach (var definition in Definitions)
            {
                if (string.Equals(definition.Id, candidate, StringComparison.OrdinalIgnoreCase))
                    return definition.Id;
            }
        }

        return EndlessId;
    }

    public static string ResolveSelection(string id, bool tacticalOnly)
        => tacticalOnly ? TacticalId : NormalizeId(id);

    public static bool CanSelect(string id, bool tacticalOnly)
        => !tacticalOnly
           || string.Equals(NormalizeId(id), TacticalId, StringComparison.Ordinal);
}

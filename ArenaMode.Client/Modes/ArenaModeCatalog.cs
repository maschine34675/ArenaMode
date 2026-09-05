using System;
using System.Collections.Generic;

namespace ArenaMode.Modes;

internal sealed class ArenaModeDefinition
{
    public ArenaModeDefinition(string id, string label, string description)
    {
        Id = id;
        Label = label;
        Description = description;
    }

    public string Id { get; }
    public string Label { get; }
    public string Description { get; }
}

/// <summary>Stable mode ids and the copy shown by the native map-screen picker.</summary>
internal static class ArenaModeCatalog
{
    public const string EndlessId = "endless";
    public const string RoundsId = "rounds";
    public const string GunGameId = "gungame";
    public const string TacticalId = "tactical";
    public const string TeamDeathmatchId = "teamdeathmatch";

    private static readonly IReadOnlyList<ArenaModeDefinition> Definitions =
        Array.AsReadOnly(new[]
        {
            new ArenaModeDefinition(EndlessId, "ENDLESS",
                "Continuous deathmatch with instant respawns."),
            new ArenaModeDefinition(RoundsId, "ROUNDS",
                "Score-limited rounds with a break between them."),
            new ArenaModeDefinition(GunGameId, "GUN GAME",
                "Advance through the weapon ladder with every kill."),
            new ArenaModeDefinition(TacticalId, "TACTICAL",
                "Single-life rounds with bought or carried equipment."),
            new ArenaModeDefinition(TeamDeathmatchId, "TEAM DM",
                "USEC versus BEAR teams with instant respawns. No companions required."),
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

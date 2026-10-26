using ArenaMode.Shared;
using System;
using System.Collections.Generic;

namespace ArenaMode.Modes;

/// <summary>
/// One selectable Arena mode and the copy the map-screen picker shows for it.
///
/// ONE VISIBLE COPY SCHEMA for every mode (2026-10-09), so the three mode facts
/// read alike and have a short copy budget for their 218 px value column at
/// 17 pt. Native font fit is verified separately. The screen shows OBJECTIVE,
/// RESPAWN and EQUIPMENT after the description; choices sit below these facts.
/// tools/NativeModeSelectionChecks enforces it for every entry in
/// <see cref="ArenaModeCatalog.All"/>, and tools/TdmMatchMenuChecks enforces
/// the same limits on what the view actually renders:
/// <list type="bullet">
/// <item><see cref="Rules"/>: the objective, at most three words and at most
/// 18 characters ("Most kills", "Survive the waves"). The roster ("1v5",
/// "5v5") is not part of it: Tactical's row label already shows it.</item>
/// <item><see cref="Respawn"/>: exactly "Instant", "Next round", or "None". The view may
/// show "After killcam" instead while a killcam is selected and playable.</item>
/// <item><see cref="Equipment"/>: the loadout source, at most three words and
/// at most 18 characters. The view may show "PMC entry gear" (protected 5v5) or
/// <c>ArenaEquipmentChoice.OwnGearLabel</c> (own gear chosen).</item>
/// <item><see cref="Description"/>: at most 80 characters, ending with a period,
/// rendered in a two-line block. Variant-specific wording follows the same
/// footprint, rather than relying on an allowed row's disabled tooltip.</item>
/// </list>
/// A word is a whitespace-separated token that contains a letter or digit, so
/// the slash in "Buy / retain kit" is punctuation, not a word.
/// </summary>
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
    public const string DominationId = ArenaPopulationPolicy.Domination;
    // Same reasoning as TacticalId/TeamDeathmatchId above: taken from the
    // shared population policy, which this id also controls (Zombie owns its
    // entire population and turns on the native infection event), so a rename
    // here that did not reach ArenaPopulationPolicy would compile and quietly
    // stop closing the server's quiet clone for it.
    public const string ZombieId = ArenaPopulationPolicy.Zombie;

    private static readonly IReadOnlyList<ArenaModeDefinition> Definitions =
        Array.AsReadOnly(new[]
        {
            new ArenaModeDefinition(GunGameId, "GUN GAME",
                "Each kill advances your weapon. The ladder repeats.",
                "Kill to advance", "Instant", "Weapon ladder"),
            new ArenaModeDefinition(DeathmatchId, "DEATHMATCH",
                "Everyone fights everyone. Set limits to end rounds.",
                "Most kills", "Instant", "Arena kits"),
            new ArenaModeDefinition(TeamDeathmatchId, "TEAM DM",
                "USEC versus BEAR. The team with more kills wins.",
                "Most team kills", "Instant", "Arena kits"),
            new ArenaModeDefinition(DominationId, "DOMINATION (WIP)",
                "Hold the map's control points. First to 200 wins; 10-minute limit.",
                "Hold objectives", "Instant", "Arena kits"),
            new ArenaModeDefinition(TacticalId, "TACTICAL (WIP)",
                "Eliminate the enemy squad. PitFireTeam supports 1-4 companions.",
                "Round elimination", "Next round", "Buy / retain kit"),
            new ArenaModeDefinition(ZombieId, "ZOMBIES (WIP)",
                "Ammo refills each wave. Optional PitFireTeam support (1-4 companions).",
                "Survive the waves", "None", "Arena kits"),
        });

    internal const float ModePanelTop = 168f;
    // Keep the accepted footprint: the area/participant panel sits immediately
    // below this one, including all four Customs choices at a 1080-unit height.
    internal static float ModePanelHeight(string mode) => NormalizeId(mode) switch
    {
        DeathmatchId or ZombieId => 596f,
        TeamDeathmatchId or DominationId => 584f,
        _ => 530f,
    };

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
        => tacticalOnly && !CanSelect(id, true) ? TacticalId : NormalizeId(id);

    public static bool CanSelect(string id, bool tacticalOnly)
        => !tacticalOnly
           || string.Equals(NormalizeId(id), TacticalId, StringComparison.Ordinal)
           || string.Equals(NormalizeId(id), ZombieId, StringComparison.Ordinal);
}

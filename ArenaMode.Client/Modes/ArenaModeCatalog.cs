using ArenaMode.Shared;
using System;
using System.Collections.Generic;

namespace ArenaMode.Modes;

/// <summary>
/// One selectable Arena mode and the copy the map-screen picker shows for it.
///
/// ONE COPY SCHEMA for every mode (2026-10-08), so the three MATCH RULES facts
/// read alike and always fit their 218 px value column at 17 pt without an
/// ellipsis. tools/NativeModeSelectionChecks enforces it for every entry in
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
/// <item><see cref="Description"/>: one sentence of at most 80 characters that
/// ends with a period. It is not displayed today: the picker's native
/// <c>HoverTooltipArea</c> only shows a message while its row is LOCKED
/// (<c>SetUnlockStatus(true)</c> disables the component, decompile
/// EFT.UI/HoverTooltipArea.cs:131-135), so an allowed row has no tooltip. It
/// stays in the schema so a future display needs no copy pass.</item>
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
                "Free-for-all, or everyone against you, with optional rounds and limits.",
                "Most kills", "Instant", "Arena kits"),
            new ArenaModeDefinition(GunGameId, "GUN GAME",
                "Every kill moves you one rung up the weapon ladder.",
                "Kill to advance", "Instant", "Weapon ladder"),
            new ArenaModeDefinition(TacticalId, "TACTICAL",
                "Round-based squad fights: solo 1v5, or 5v5 with four PitFireTeam companions.",
                "Round elimination", "Next round", "Buy / retain kit"),
            new ArenaModeDefinition(TeamDeathmatchId, "TEAM DM",
                "USEC versus BEAR with instant respawns; no companions required.",
                "Most team kills", "Instant", "Arena kits"),
            new ArenaModeDefinition(ZombieId, "ZOMBIES",
                "Survive endless infected waves; solo ends when you die.",
                "Survive the waves", "None", "Arena kits"),
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
        => tacticalOnly && !CanSelect(id, true) ? TacticalId : NormalizeId(id);

    public static bool CanSelect(string id, bool tacticalOnly)
        => !tacticalOnly
           || string.Equals(NormalizeId(id), TacticalId, StringComparison.Ordinal)
           || string.Equals(NormalizeId(id), ZombieId, StringComparison.Ordinal);
}

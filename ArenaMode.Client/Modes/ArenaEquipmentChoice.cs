using ArenaMode.Shared;
using System;

namespace ArenaMode.Modes;

/// <summary>
/// Pure, mode-keyed table for item 1 of the 2026-10-06 follow-up ("own gear to
/// the left"): which modes offer a player-facing equipment CHOICE at all, and
/// what the two options are called. Today only Zombie offers a choice —
/// Tactical already has its own protected-team own-gear semantics (automatic,
/// driven by a complete PitFireTeam roster, never a toggle) and must not grow
/// a row here. Adding Deathmatch/Gun Game later is a change to
/// <see cref="IsOfferedFor"/>'s comparison, not a new UI branch: the left
/// panel row (<see cref="Session.ArenaEquipmentChoiceState"/>/
/// <see cref="Session.ArenaEquipmentChoiceView"/>) and the EQUIPMENT fact text
/// in <see cref="ArenaModeSelectionView"/> both read only this table plus
/// <see cref="Config.Settings"/>'s own per-mode backing-entry dispatch
/// (<c>EquipmentChoiceEntry</c>), never a mode id literal of their own.
///
/// Dependency-free like <see cref="ZombieOwnGearGate"/> and
/// <c>Kits.KitPreloadPlan</c>'s own mode comparisons (see their doc comments):
/// compares against <see cref="ArenaPopulationPolicy.Zombie"/> rather than
/// <c>ArenaModeCatalog.ZombieId</c> so this file needs nothing beyond
/// <c>ArenaMode.Shared</c> and can link into <c>ArenaMode.Tests</c> the same
/// way.
/// </summary>
internal static class ArenaEquipmentChoice
{
    internal const string ArenaKitsLabel = "Arena kits";
    internal const string OwnGearLabel = "Own gear";

    /// <summary>
    /// True iff <paramref name="mode"/> names a mode that offers the player a
    /// choice between Arena kits and their own gear. Case- and
    /// whitespace-insensitive, matching every other frozen mode-id comparison
    /// in this codebase (see <see cref="ZombieOwnGearGate.IsSelectedFor"/>).
    /// </summary>
    internal static bool IsOfferedFor(string mode)
        => !string.IsNullOrWhiteSpace(mode)
           && string.Equals(mode.Trim(), ArenaPopulationPolicy.Zombie, StringComparison.OrdinalIgnoreCase);

    internal static string Label(bool ownGear) => ownGear ? OwnGearLabel : ArenaKitsLabel;
}

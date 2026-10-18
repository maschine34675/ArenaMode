using ArenaMode.Shared;
using System;

namespace ArenaMode.Modes;

/// <summary>
/// The equipment selector serves solo Zombies and protected Tactical. It changes
/// only the PMC loadout; PFT Zombies and solo Tactical keep their existing rules.
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
    internal static bool IsOfferedFor(string mode, bool protectedTeam = false)
        => !string.IsNullOrWhiteSpace(mode)
           && string.Equals(mode.Trim(), protectedTeam ? ArenaPopulationPolicy.Tactical : ArenaPopulationPolicy.Zombie,
               StringComparison.OrdinalIgnoreCase);

    internal static string Label(bool ownGear) => ownGear ? OwnGearLabel : ArenaKitsLabel;
}

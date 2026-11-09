namespace ArenaMode.Modes;

/// <summary>
/// The labels of the inline equipment selector in the left mode panel and the
/// question which mode shows it. The table itself is
/// <see cref="ArenaOwnGearPolicy"/>'s, so the row and the raid can never
/// disagree about who may bring their own gear: every mode but Gun Game solo,
/// Tactical alone with a protected PitFireTeam squad. The row changes only the
/// local PMC's loadout; PFT companions keep their own equipment either way.
/// </summary>
internal static class ArenaEquipmentChoice
{
    internal const string ArenaKitsLabel = "Arena kits";
    internal const string OwnGearLabel = "Own gear";

    /// <summary>
    /// True iff <paramref name="mode"/> offers the player a choice between
    /// Arena kits and their own gear, for the given roster. Case- and
    /// whitespace-insensitive like every other frozen mode-id comparison.
    /// </summary>
    internal static bool IsOfferedFor(string mode, bool protectedTeam = false)
        => ArenaOwnGearPolicy.IsOffered(mode, protectedTeam);

    internal static string Label(bool ownGear) => ownGear ? OwnGearLabel : ArenaKitsLabel;

    /// <summary>
    /// The row's tooltip: what each choice means in the selected mode, since
    /// the consequence differs (solo Tactical suspends its kit economy, a
    /// protected squad changes only the PMC, the respawning modes restore the
    /// entry loadout on every life).
    /// </summary>
    internal static string Describe(bool ownGear, string mode, bool protectedTeam)
    {
        if (!ownGear)
            return protectedTeam
                ? "A supplied Arena kit for your PMC; companions keep their own gear."
                : "A supplied Arena kit each life.";
        if (protectedTeam)
            return "Your PMC's own equipment, protected: restored after a death at the next round. Companions keep theirs either way.";
        string id = (mode ?? string.Empty).Trim().ToLowerInvariant();
        if (id == ArenaMode.Shared.ArenaPopulationPolicy.Tactical)
            return "Your own equipment, protected: restored at every round start and after a death. No buy phase, no roubles.";
        if (id == ArenaMode.Shared.ArenaPopulationPolicy.Zombie)
            return "Your own equipment, protected: restored after each cleared wave. One life.";
        if (id == ArenaMode.Shared.DeathmatchSelection.ModeId)
            return "Your own equipment, protected: restored on every respawn and at a round break. Items picked up are not kept.";
        return "Your own equipment, protected: restored on every respawn. Items picked up are not kept.";
    }
}

using ArenaMode.Shared;
using System;

namespace ArenaMode.Modes;

/// <summary>
/// Pure decision layer for item 4 of the 2026-10-06 follow-up ("own protected
/// gear"): whether Zombie's own-gear option is the active equipment path for
/// a raid, given the setting as frozen at arm and the raid's mode id. No
/// EFT/Unity types, so ArenaMode.Tests can link this file directly (same
/// convention as ZombieWavePolicy/ZombieRoleMix/ZombieMeleeGate — see their
/// own doc comments and ZOMBIE-MODE-REVIEW.md, "Native melee is suppressed by
/// SAIN's global prefix").
///
/// <see cref="Session.ArenaSession"/> freezes the F12 setting once, the exact
/// moment <c>GunGameLadderSelection</c> is frozen (<c>MarkArmed</c>), so a
/// mid-loading edit cannot desync the loading-screen kit-preload skip from
/// the live raid's respawn/refill/secure-container behaviour. This gate is
/// the ONE place that turns that frozen bool plus a mode id into "own gear is
/// active for THIS raid" — called identically for the loading-screen check
/// (<c>ArenaSession.ZombieOwnGearArmed</c>, against <c>ArmedMode</c>) and the
/// live-raid check (<c>ArenaSession.ZombieOwnGearActive</c>, against
/// <c>ActiveMode</c>), so the two can never disagree about the comparison
/// itself.
/// </summary>
internal static class ZombieOwnGearGate
{
    /// <summary>
    /// True iff <paramref name="ownGearSelection"/> (the frozen F12 value) is
    /// set AND <paramref name="mode"/> names the Zombie mode. Compared
    /// against <see cref="ArenaPopulationPolicy.Zombie"/> rather than
    /// <c>ArenaModeCatalog.ZombieId</c> so this file stays as dependency-free
    /// as <c>KitPreloadPlan</c>'s own mode comparisons (same reasoning as
    /// that class's own doc comment: the two constants are the same string by
    /// construction — <c>ArenaModeCatalog.ZombieId = ArenaPopulationPolicy.
    /// Zombie</c>). Case- and whitespace-insensitive, matching every other
    /// frozen mode-id comparison in this codebase (e.g.
    /// <c>ArenaModeCatalog.NormalizeId</c> and the armed-map comparison in
    /// <c>ArenaSession.OnRaidStarted</c>) — a mode id that arrives differently
    /// cased must not silently disable the protection the player explicitly
    /// asked for.
    /// </summary>
    internal static bool IsSelectedFor(string mode, bool ownGearSelection)
        => ownGearSelection
           && !string.IsNullOrWhiteSpace(mode)
           && string.Equals(mode.Trim(), ArenaPopulationPolicy.Zombie, StringComparison.OrdinalIgnoreCase);
}

using ArenaMode.Shared;
using System;

namespace ArenaMode.Modes;

/// <summary>
/// Pure decision layer for the player's own-gear option: which modes offer the
/// player's real, protected equipment in place of an Arena kit, and whether the
/// selection frozen at arm is the active equipment path for a raid. Grew out of
/// Zombie's own-gear gate (item 4 of the 2026-10-06 follow-up) when the option
/// was offered to every mode on 2026-11-09; the contract is
/// docs/reviews/OWN-GEAR-ALL-MODES-20261109.md. No EFT/Unity types, so
/// ArenaMode.Tests links this file directly (same convention as
/// ZombieWavePolicy/ZombieRoleMix/ZombieMeleeGate).
///
/// Gun Game is deliberately not in the table: its ladder IS the loadout (a rung
/// is a whole kit, or the earned weapon merged onto the base kit by
/// KitMerge.WithPrimaryWeapon), so there is no "own" equipment a rung could be
/// merged into. Solo Tactical is not in it either (user decision, 2026-11-09,
/// after a day with the option): its buy economy IS the mode, the gear is
/// earned in the match, so a brought loadout would hollow it out. Both keep
/// their plain fact ("Assigned weapons", "Buy / retain kit").
///
/// Protected PitFireTeam squads are a different axis and never read
/// <see cref="IsSelectedFor"/>: PFT Zombies keeps own gear without a choice,
/// PFT Tactical decides through Settings.TacticalTeamOwnGear and its own
/// entry-kit machinery (TacticalTeamKitEntry, TacticalMode's team snapshot).
/// ArenaSession.MarkArmed freezes false for a protected Tactical squad and
/// true for a protected Zombie squad, so the live gate agrees with them.
///
/// <see cref="Session.ArenaSession"/> freezes the setting once, the exact
/// moment GunGameLadderSelection is frozen (MarkArmed), so a mid-loading edit
/// cannot desync the loading-screen kit-preload skip from the live raid's
/// respawn, round-break reissue, refill and secure-container behaviour. This
/// class is the ONE place that turns that frozen bool plus a mode id into
/// "own gear is active for THIS raid", called identically for the
/// loading-screen check (ArenaSession.OwnGearArmed, against ArmedMode) and the
/// live-raid check (ArenaSession.OwnGearActive, against ActiveMode), so the two
/// can never disagree about the comparison itself.
/// </summary>
internal static class ArenaOwnGearPolicy
{
    /// <summary>
    /// The modes whose loadout the player may bring themselves, spelled as the
    /// frozen mode ids are (ArenaModeCatalog.NormalizeId runs before arming, so
    /// the legacy "endless"/"rounds" ids never reach a frozen selection). Taken
    /// from the shared constants rather than ArenaModeCatalog so this file stays
    /// as dependency-free as KitPreloadPlan's own mode comparisons; the catalog
    /// takes its ids from the same constants, so the two cannot drift apart.
    /// </summary>
    private static readonly string[] SoloModes =
    {
        DeathmatchSelection.ModeId,
        ArenaPopulationPolicy.TeamDeathmatch,
        ArenaPopulationPolicy.Domination,
        ArenaPopulationPolicy.Zombie,
    };

    /// <summary>
    /// True iff the map screen offers the Arena kits / Own gear choice for
    /// <paramref name="mode"/>: solo, Deathmatch, Team DM, Domination and
    /// Zombies; with a protected PitFireTeam squad, Tactical alone (PFT Zombies
    /// keeps own gear without a choice and shows the plain "PMC entry gear"
    /// fact instead).
    /// </summary>
    internal static bool IsOffered(string mode, bool protectedTeam)
        => protectedTeam ? Is(mode, ArenaPopulationPolicy.Tactical) : IsSoloMode(mode);

    /// <summary>
    /// True iff <paramref name="ownGearSelection"/> (the value frozen at arm)
    /// is set AND <paramref name="mode"/> names a mode whose loadout the player
    /// may bring. Case- and whitespace-insensitive, matching every other frozen
    /// mode-id comparison in this codebase (ArenaModeCatalog.NormalizeId, the
    /// armed-map comparison in ArenaSession.OnRaidStarted): a mode id that
    /// arrives differently cased must not silently disable the protection the
    /// player explicitly asked for.
    /// </summary>
    internal static bool IsSelectedFor(string mode, bool ownGearSelection)
        => ownGearSelection && IsSoloMode(mode);

    /// <summary>
    /// The value ArenaSession freezes at arm for <paramref name="mode"/>: the
    /// player's saved per-mode preference when they play alone, and by roster
    /// alone for a protected PitFireTeam squad (Zombies keeps own gear without
    /// a choice, Tactical decides through ArenaSession.TacticalTeamOwnGearSelection
    /// and its own entry-kit machinery, so false here). Same trimmed,
    /// case-insensitive comparison as the gate, so the arm and the gate cannot
    /// drift apart on a differently spelled id.
    /// </summary>
    internal static bool SelectionAtArm(string mode, bool protectedTeam, bool soloPreference)
        => protectedTeam ? Is(mode, ArenaPopulationPolicy.Zombie) : soloPreference;

    private static bool IsSoloMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return false;
        foreach (var candidate in SoloModes)
            if (Is(mode, candidate)) return true;
        return false;
    }

    private static bool Is(string mode, string id)
        => !string.IsNullOrWhiteSpace(mode)
           && string.Equals(mode.Trim(), id, StringComparison.OrdinalIgnoreCase);
}

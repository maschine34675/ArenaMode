#nullable enable

using System;

namespace ArenaMode.Shared;

/// <summary>
/// Which arena modes own their entire bot population, and what follows from it.
///
/// Three modes count the bodies they spawned and nothing else: Team Deathmatch
/// reserves equal faction slots, Tactical counts one round's squad, Zombie
/// counts its own endless-wave infected. For those the server empties the
/// location clone's waves, silences its boss spawns and closes ABPS's spawn
/// window, so no bot can arrive that the mode did not ask for. Zombie
/// additionally turns the clone's native infection event on (see
/// ArenaLocationMutator/ZombieLocationSettings) — that part is unique to it,
/// but owning its population is the same rule as the other two. Deathmatch
/// (including legacy Endless/Rounds ids) and Gun Game spawn scavs, from ABPS's
/// pump or from the generated wave plan, and must keep every role they might
/// draw.
///
/// The rule used to live in the server's location mutator alone, until the
/// client needed it too; it is linked into both halves so the two cannot
/// drift. A mode this file does not recognise is treated as NOT owning its
/// population — which is the cautious answer on the client, where it means the
/// native preload is left alone, but is not a no-op on the server: the loud
/// branch of the mutator is the arena's most invasive one, raising PMC chances
/// to certainty and generating assault waves. Adding a mode means deciding
/// which branch it belongs in, not leaving it to a default.
/// </summary>
public static class ArenaPopulationPolicy
{
    public const string TeamDeathmatch = "teamdeathmatch";
    public const string Tactical = "tactical";
    public const string Zombie = "zombie";

    /// <summary>
    /// True when the mode spawns every bot itself and an ambient pump, a
    /// scheduled wave or a boss would be a body its scoreboard cannot explain.
    /// </summary>
    public static bool OwnsEntirePopulation(string? mode)
        => string.Equals(mode, Tactical, StringComparison.OrdinalIgnoreCase)
           || string.Equals(mode, TeamDeathmatch, StringComparison.OrdinalIgnoreCase)
           || string.Equals(mode, Zombie, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the raid being loaded may skip the client's raid-start profile
    /// preload entirely.
    ///
    /// Team Deathmatch ONLY, and not simply every mode that owns its
    /// population. Both it and Tactical spawn their own squads, but only Team
    /// Deathmatch also throws the pre-generated PMC profiles away: its
    /// difficulty clock deletes the matching pool entries so each bot is
    /// generated fresh against the current tier. Tactical draws from that pool
    /// and would trade the loading screen for a server round trip on every
    /// squad member — which may well be the better trade, but it is untested,
    /// and Tactical with PitFireTeam is the least-walked path in the mod.
    ///
    /// Deliberately asked from the ARMED state rather than from IsActive: the
    /// preload runs inside the loading screen, and IsActive is only latched
    /// once the raid is up and the server has acknowledged the arm token —
    /// long after. Gating this on IsActive would have made it a silent no-op.
    ///
    /// The map is compared as well, so a raid started on a different map than
    /// the one armed keeps its native preload. What remains uncovered is the
    /// narrow case where the armed map matches but the server then refuses the
    /// arm token, or Fika is present: that raid falls back to ordinary with an
    /// empty profile pool. It is not a broken raid — a pool miss asks the
    /// server for three more and carries on — but it IS one of the two things
    /// in the mod that can act on a raid which does not become an arena raid
    /// (the other is the loading-screen kit preload,
    /// KitBundlePreloader.BeginRaidStart), and both are recorded in AGENTS.md
    /// beside the rule they bend.
    ///
    /// Zombie deliberately does NOT get this skip, even though it also owns
    /// its population and also empties Waves/BossLocationSpawn on the clone.
    /// What ArenaProfilePreloadPatch clears is the GENERIC assault/marksman/PMC
    /// pairs that BotProfileClient/NonWavesSpawnScenario preload regardless of
    /// the clone's own Waves — Team Deathmatch can throw those away only
    /// because its difficulty clock (ArenaBotDifficultyClockPatch) deletes the
    /// matching pool entries anyway, so nothing is lost. Zombie has no such
    /// clock, and the real cost this mode's own 498-profile native preload
    /// would add is an entirely different native path
    /// (BotHalloweenWithZombies.GetProfilesOnStart, decompile-verified) that a
    /// dedicated client-side prefix replaces directly — see the zombie client
    /// stage's native event bridge. Clearing the generic pairs here as well
    /// would save nothing Zombie uses and would risk the same kind of silent
    /// bot-side breakage Tactical is excluded to avoid. If a GPU raid later
    /// shows the generic preload is still a measurable cost for Zombie, add it
    /// here with its own evidence — do not assume it transfers from Team
    /// Deathmatch's case.
    /// </summary>
    public static bool SkipsProfilePreload(bool armedForNextRaid, string? armedMap, string? armedMode, string? loadingMap)
        => armedForNextRaid
           && !string.IsNullOrEmpty(armedMap)
           && !string.IsNullOrEmpty(loadingMap)
           && string.Equals(armedMap, loadingMap, StringComparison.OrdinalIgnoreCase)
           && string.Equals(armedMode, TeamDeathmatch, StringComparison.OrdinalIgnoreCase);
}

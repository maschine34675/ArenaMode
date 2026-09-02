using ArenaMode.Combat;
using EFT;

namespace ArenaMode.Modes;

/// <summary>
/// One arena game mode. The match owns scoring, kill bookkeeping and HUD
/// plumbing; the mode owns win conditions and mode-specific reactions.
/// Instances live for exactly one raid (created at match start).
/// </summary>
public interface IArenaMode
{
    /// <summary>Stable id used by config selection ("endless", "rounds", "gungame").</summary>
    string Id { get; }

    string DisplayName { get; }

    /// <summary>True once the mode's win condition is met (Endless: never).</summary>
    bool IsComplete { get; }

    /// <summary>
    /// Mode-specific HUD line rendered under the score (e.g. round timer,
    /// gun-game tier). Null/empty hides the line.
    /// </summary>
    string HudStatusLine { get; }

    /// <summary>
    /// How many seconds of death replay this mode wants, or 0 for none.
    ///
    /// Zero everywhere the player respawns instantly: a replay would play over a
    /// live body already standing somewhere else. A mode that makes death COST
    /// something — a wait for the next round — is the one place a replay has
    /// room, and it says so here. Read through ArenaSession.ReplaySeconds, which
    /// is the surface KillcamReplay reflects on.
    /// </summary>
    float DeathReplaySeconds { get; }

    /// <summary>
    /// True while the mode wants the player to stay dead.
    ///
    /// The arena's whole identity is instant respawn, so this is false almost
    /// everywhere. Tactical inverts it on purpose: dying costs you the round,
    /// and you come back when the next one starts. The respawn routine waits on
    /// this BEFORE it picks a kit, so whatever the player buys while dead is
    /// what they are handed.
    /// </summary>
    bool HoldRespawn { get; }

    /// <summary>
    /// True when the mode spawns its own bots and the ambient top-up must stay
    /// out of the way.
    ///
    /// Asked, not announced. A mode that switched a flag on at match start would
    /// depend on running after ArenaSession's raid-start reset, which clears the
    /// burst service — an ordering that holds today and would break silently the
    /// day the two subscriptions are reordered. Asking the running mode cannot
    /// go out of date.
    /// </summary>
    bool ControlsOwnSpawns { get; }

    void OnMatchStart(ArenaMatch match);

    /// <summary>Per-frame while the match runs (main thread).</summary>
    void Tick(float deltaTime);

    /// <summary>Every scored kill, local and (if captured) bot-vs-bot.</summary>
    void OnKill(ArenaKill kill);

    void OnLocalDeath();

    /// <summary>The replacement local-player body after it is fully wired into the live raid.</summary>
    void OnLocalRespawn(Player player);

    void OnMatchEnd();
}

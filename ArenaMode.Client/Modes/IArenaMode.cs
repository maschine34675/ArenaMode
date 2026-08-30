using ArenaMode.Combat;

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

    void OnMatchStart(ArenaMatch match);

    /// <summary>Per-frame while the match runs (main thread).</summary>
    void Tick(float deltaTime);

    /// <summary>Every scored kill, local and (if captured) bot-vs-bot.</summary>
    void OnKill(ArenaKill kill);

    void OnLocalDeath();

    void OnLocalRespawn();

    void OnMatchEnd();
}

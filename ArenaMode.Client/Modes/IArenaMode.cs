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

    void OnMatchStart(ArenaMatch match);

    /// <summary>Per-frame while the match runs (main thread).</summary>
    void Tick(float deltaTime);

    /// <summary>Every scored kill, local and (if captured) bot-vs-bot.</summary>
    void OnKill(ArenaKill kill);

    void OnLocalDeath();

    void OnLocalRespawn();

    void OnMatchEnd();
}

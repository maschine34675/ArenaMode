namespace ArenaMode.Modes;

/// <summary>
/// Audio-only lifecycle cues. These deliberately do not use ArenaMatch.OnCallout:
/// kill callouts are also rendered by the HUD, while round voice lines are not.
/// </summary>
internal enum ArenaAnnouncerCue
{
    RoundStart,
    RoundWon,
    RoundLost,
}

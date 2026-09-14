namespace ArenaMode.Modes;

/// <summary>
/// Non-HUD lifecycle cues. These deliberately do not use ArenaMatch.OnCallout:
/// kill callouts are rendered by the HUD, while round audio and lighting are not.
/// </summary>
internal enum ArenaAnnouncerCue
{
    RoundStart,
    RoundWon,
    RoundLost,
    LastManStanding,
    MatchWon,
    MatchLost,
    PrepareToFight,
    Three,
    Two,
    One,
    CancelStatus,
    TakenTheLead,
    LostTheLead,
    TiedForTheLead,
    OneMinuteRemaining,
    ThirtySecondsRemaining,
    TenSecondsRemaining,
    FiveKillsRemaining,
    OneKillRemaining,
    MatchPoint,
    FinalRound,
    FinalWeapon,
    LadderComplete,
    Draw,
    Shutdown,
    Clutch,
}

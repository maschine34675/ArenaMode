namespace ArenaMode.Audio;

/// <summary>Copied, presentation-only mode progress; null means not applicable.</summary>
internal struct ArenaAnnouncementSnapshot
{
    internal bool Active;
    internal int Epoch;
    internal double Now;
    internal bool Preparing;
    internal int? Countdown;
    internal bool Live;
    internal bool Terminal;
    internal bool Draw;
    internal double? SecondsRemaining;
    internal int? KillsRemaining;
    // Relative to the local player/team, using the actual round win criterion.
    internal int? Lead; // -1 behind, 0 tied, +1 ahead; null when irrelevant/0:0
    internal bool FinalRound;
    internal bool MatchPoint;
    internal int? WeaponTier; // zero-based, only once actually equipped
    internal int? WeaponCount;
    internal int? LadderCycle;
}

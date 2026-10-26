using System;
using ArenaMode.Audio;
using UnityEngine;

namespace ArenaMode.Modes;

public sealed partial class TeamDeathmatchMode
{
    internal ArenaAnnouncementSnapshot AnnouncementSnapshot()
    {
        if (_ended || !MatchRulesReady || !IsCurrentRaid() || _match == null) return default;
        int friendly = FriendlyTeamScore;
        int opponent = OpponentTeamScore;
        return new ArenaAnnouncementSnapshot
        {
            Active = true, Epoch = 1, Now = Time.time,
            Preparing = MatchPhase == TdmMatchPhase.Assembling,
            Countdown = MatchPhase == TdmMatchPhase.Countdown ? MatchCountdownRemaining(Time.time) : null,
            Live = MatchPhase == TdmMatchPhase.Live,
            Terminal = HasResult, Draw = HasResult && MatchResult == TdmMatchResult.Draw,
            SecondsRemaining = MatchTimeLimit > 0
                ? Math.Max(0d, MatchTimeLimit - PlayingElapsed) : null,
            KillsRemaining = !IsDomination && MatchScoreLimit > 0
                ? Math.Max(0, MatchScoreLimit - Math.Max(friendly, opponent)) : null,
            Lead = Math.Max(friendly, opponent) > 0 ? friendly.CompareTo(opponent) : null,
        };
    }
}

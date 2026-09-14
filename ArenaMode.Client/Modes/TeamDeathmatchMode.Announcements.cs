using System;
using ArenaMode.Audio;
using UnityEngine;

namespace ArenaMode.Modes;

public sealed partial class TeamDeathmatchMode
{
    internal ArenaAnnouncementSnapshot AnnouncementSnapshot()
    {
        if (_ended || _rules == null || !IsCurrentRaid() || _match == null) return default;
        int friendly = _match.Scoreboard.Totals(ArenaScoreboardSide.Friendly).Kills;
        int opponent = _match.Scoreboard.Totals(ArenaScoreboardSide.Opponent).Kills;
        return new ArenaAnnouncementSnapshot
        {
            Active = true, Epoch = 1, Now = Time.time,
            Preparing = _rules.Phase == TdmMatchPhase.Assembling,
            Countdown = _rules.Phase == TdmMatchPhase.Countdown ? _rules.CountdownRemaining(Time.time) : null,
            Live = _rules.Phase == TdmMatchPhase.Live,
            Terminal = HasResult, Draw = HasResult && _rules.Result == TdmMatchResult.Draw,
            SecondsRemaining = _rules.TimeLimitSeconds > 0
                ? Math.Max(0d, _rules.TimeLimitSeconds - PlayingElapsed) : null,
            KillsRemaining = _rules.KillLimit > 0
                ? Math.Max(0, _rules.KillLimit - Math.Max(friendly, opponent)) : null,
            Lead = Math.Max(friendly, opponent) > 0 ? friendly.CompareTo(opponent) : null,
        };
    }
}

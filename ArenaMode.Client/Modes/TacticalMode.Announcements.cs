using System;
using ArenaMode.Audio;
using UnityEngine;

namespace ArenaMode.Modes;

public sealed partial class TacticalMode
{
    internal ArenaAnnouncementSnapshot AnnouncementSnapshot()
    {
        if (_match == null || _phase == Phase.Ended) return default;
        bool live = _phase == Phase.Live && (_teamArena || _soloCombatReady);
        return new ArenaAnnouncementSnapshot
        {
            Active = true, Epoch = RoundNumber, Now = Time.time,
            Preparing = _phase == Phase.Buy || _phase == Phase.Staging,
            Live = live,
            SecondsRemaining = live ? Math.Max(0d, _phaseEndsAt - Time.time) : null,
            MatchPoint = _teamArena && _teamRoundsToWin > 0
                && (RoundsWon == _teamRoundsToWin - 1 || RoundsLost == _teamRoundsToWin - 1),
            FinalRound = _teamArena && _teamRoundsToWin > 0
                && RoundsWon == _teamRoundsToWin - 1 && RoundsLost == _teamRoundsToWin - 1,
        };
    }
}

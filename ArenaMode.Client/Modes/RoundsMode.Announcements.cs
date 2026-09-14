using ArenaMode.Audio;
using UnityEngine;

namespace ArenaMode.Modes;

public sealed partial class RoundsMode
{
    internal ArenaAnnouncementSnapshot AnnouncementSnapshot()
    {
        if (_ended || _rules == null || !IsCurrentRaid()) return default;
        _rules.ReadAnnouncementScore(out int? killsRemaining, out int? lead);
        return new ArenaAnnouncementSnapshot
        {
            Active = true, Epoch = _rules.RoundNumber, Now = Time.time,
            Preparing = WaitingForStart || InIntermission,
            Live = _rules.Phase == DeathmatchPhase.Live,
            Terminal = HasResult,
            Draw = HasResult ? _rules.Outcome == DeathmatchOutcome.Draw
                : InIntermission && _rules.RoundOutcome == DeathmatchOutcome.Draw,
            SecondsRemaining = _options.Seconds > 0 ? _rules.TimeRemaining(Time.time) : null,
            KillsRemaining = killsRemaining, Lead = lead,
            FinalRound = _options.Rounds > 0 && _rules.RoundNumber == _options.Rounds,
        };
    }
}

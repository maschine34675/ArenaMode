using ArenaMode.Audio;
using UnityEngine;

namespace ArenaMode.Modes;

public sealed partial class RoundsMode
{
    internal ArenaAnnouncementSnapshot AnnouncementSnapshot()
    {
        if (_ended || _rules == null || !IsCurrentRaid()) return default;
        _rules.ReadAnnouncementScore(out int? killsRemaining, out int? lead);
        bool preparing = WaitingForStart || InIntermission;
        // Preparation belongs to the round it announces: rules increment only
        // on StartRound. Keeping the old number here makes ordinary 0->1 (and
        // intermission->next round) look like an invalidated audio epoch.
        int epoch = preparing && _rules.RoundNumber < int.MaxValue
            ? _rules.RoundNumber + 1 : _rules.RoundNumber;
        return new ArenaAnnouncementSnapshot
        {
            Active = true, Epoch = epoch, Now = Time.time,
            Preparing = preparing,
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

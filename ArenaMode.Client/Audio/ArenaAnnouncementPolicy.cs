using System;
using ArenaMode.Modes;

namespace ArenaMode.Audio;

/// <summary>
/// Presentation state only. The mode supplies its real win criteria and phase;
/// this policy neither derives a result from scores nor changes any match state.
/// </summary>
internal sealed class ArenaAnnouncementPolicy
{
    internal const double LeadCooldownSeconds = 3d;

    private bool _active;
    private int _epoch;
    private bool _preparing;
    private bool _prepareSpoken;
    private bool _live;
    private bool _terminal;
    private bool _enteredLive;
    private bool _drawSpoken;
    private int? _countdown;
    private int _countdownSpoken;
    private double? _seconds;
    private int? _kills;
    private int _timeSpoken;
    private int _killsSpoken;
    private bool _leadObserved;
    private int? _spokenLead;
    private double _nextLeadAt;
    private double _lastNow;
    private int? _highestCycle;
    private int _finalWeaponCycle = -1;

    internal void Reset()
    {
        _active = false;
        _epoch = 0;
        _preparing = _prepareSpoken = _live = _terminal = false;
        _enteredLive = _drawSpoken = false;
        _countdown = null;
        _countdownSpoken = _timeSpoken = _killsSpoken = 0;
        _seconds = null;
        _kills = null;
        _leadObserved = false;
        _spokenLead = null;
        _nextLeadAt = _lastNow = 0d;
        _highestCycle = null;
        _finalWeaponCycle = -1;
    }

    internal void Update(ArenaAnnouncementSnapshot snapshot, Action<ArenaAnnouncerCue> emit)
    {
        if (emit == null) throw new ArgumentNullException(nameof(emit));
        if (!snapshot.Active)
        {
            if (_active) emit(ArenaAnnouncerCue.CancelStatus);
            Reset();
            return;
        }

        bool newEpoch = _active && snapshot.Epoch != _epoch;
        if (newEpoch)
        {
            emit(ArenaAnnouncerCue.CancelStatus);
            Reset();
        }
        _active = true;
        _epoch = snapshot.Epoch;

        bool preparing = snapshot.Preparing && !snapshot.Live && !snapshot.Terminal;
        int? countdown = !snapshot.Live && !snapshot.Terminal &&
            snapshot.Countdown >= 1 && snapshot.Countdown <= 3 ? snapshot.Countdown : null;
        bool countdownEnded = _countdown.HasValue && !countdown.HasValue;
        if (!newEpoch && (countdownEnded || (_preparing && !preparing) || (_live && !snapshot.Live) ||
            (!_terminal && snapshot.Terminal)))
        {
            bool advancing = !snapshot.Terminal &&
                ((_preparing && !preparing && (countdown.HasValue || snapshot.Live)) ||
                 (_countdown.HasValue && snapshot.Live));
            emit(advancing ? ArenaAnnouncerCue.AdvanceStatus : ArenaAnnouncerCue.CancelStatus);
        }

        // Readiness can be lost within one logical round. A fresh contiguous
        // countdown may repeat digits; Prepare remains once per round/epoch.
        if (countdownEnded) _countdownSpoken = 0;
        if (preparing && !_prepareSpoken)
        {
            _prepareSpoken = true;
            emit(ArenaAnnouncerCue.PrepareToFight);
        }
        if (countdown.HasValue)
        {
            int bit = 1 << countdown.Value;
            if ((_countdownSpoken & bit) == 0)
            {
                _countdownSpoken |= bit;
                emit(countdown.Value == 3 ? ArenaAnnouncerCue.Three :
                    countdown.Value == 2 ? ArenaAnnouncerCue.Two : ArenaAnnouncerCue.One);
            }
        }

        if (snapshot.Live && !snapshot.Terminal && !_enteredLive)
        {
            _enteredLive = true;
            if (snapshot.FinalRound) emit(ArenaAnnouncerCue.FinalRound);
            else if (snapshot.MatchPoint) emit(ArenaAnnouncerCue.MatchPoint);
        }
        if (snapshot.Draw && !snapshot.Live && !_drawSpoken)
        {
            _drawSpoken = true;
            emit(ArenaAnnouncerCue.Draw);
        }

        // Completion survives the mode clearing its equipped tier at a wrap or
        // terminal transition. Initial observation is a baseline, never catch-up.
        if (snapshot.Live || (snapshot.Terminal && _live)) UpdateLadder(snapshot, emit);
        if (snapshot.Live && !snapshot.Terminal)
        {
            UpdateTime(snapshot.SecondsRemaining, emit);
            UpdateKills(snapshot.KillsRemaining, emit);
            UpdateLead(snapshot.Lead, snapshot.Now, emit);
        }
        else
        {
            _seconds = null;
            _kills = null;
            _leadObserved = false;
            _spokenLead = null;
        }

        _preparing = preparing;
        _countdown = countdown;
        _live = snapshot.Live && !snapshot.Terminal;
        _terminal = snapshot.Terminal;
    }

    private void UpdateTime(double? seconds, Action<ArenaAnnouncerCue> emit)
    {
        if (!seconds.HasValue || double.IsNaN(seconds.Value) || double.IsInfinity(seconds.Value))
        {
            _seconds = null;
            return;
        }
        double current = Math.Max(0d, seconds.Value);
        ArenaAnnouncerCue? cue = null;
        // Visit urgent thresholds first. Consume skipped larger thresholds so a
        // clock adjustment cannot later speak an already expired warning.
        ConsiderThreshold(_seconds, current, 10, 1, ref _timeSpoken, ArenaAnnouncerCue.TenSecondsRemaining, ref cue);
        ConsiderThreshold(_seconds, current, 30, 2, ref _timeSpoken, ArenaAnnouncerCue.ThirtySecondsRemaining, ref cue);
        ConsiderThreshold(_seconds, current, 60, 4, ref _timeSpoken, ArenaAnnouncerCue.OneMinuteRemaining, ref cue);
        _seconds = current;
        if (cue.HasValue) emit(cue.Value);
    }

    private void UpdateKills(int? kills, Action<ArenaAnnouncerCue> emit)
    {
        if (!kills.HasValue)
        {
            _kills = null;
            return;
        }
        int current = Math.Max(0, kills.Value);
        ArenaAnnouncerCue? cue = null;
        ConsiderThreshold(_kills, current, 1, 1, ref _killsSpoken, ArenaAnnouncerCue.OneKillRemaining, ref cue);
        ConsiderThreshold(_kills, current, 5, 2, ref _killsSpoken, ArenaAnnouncerCue.FiveKillsRemaining, ref cue);
        _kills = current;
        if (cue.HasValue) emit(cue.Value);
    }

    private static void ConsiderThreshold(double? previous, double current, double threshold, int bit,
        ref int spoken, ArenaAnnouncerCue candidate, ref ArenaAnnouncerCue? cue)
    {
        if (current > threshold || (spoken & bit) != 0) return;
        spoken |= bit;
        if (previous.HasValue && previous.Value > threshold && current > 0d && !cue.HasValue)
            cue = candidate;
    }

    private void UpdateLead(int? lead, double now, Action<ArenaAnnouncerCue> emit)
    {
        if (lead < -1 || lead > 1) lead = null;
        if (double.IsNaN(now) || double.IsInfinity(now)) now = _lastNow;
        if (now < _lastNow) _nextLeadAt = now;
        _lastNow = now;

        // Joining scored progress is silent. Observing the genuine 0:0/null
        // baseline first permits the first scored lead (or nonzero tie) to speak.
        if (!_leadObserved)
        {
            _leadObserved = true;
            _spokenLead = lead;
            return;
        }
        if (!lead.HasValue)
        {
            _spokenLead = null;
            return;
        }
        // Compare against the last emitted state, not the last frame: rapid
        // transitions coalesce to the latest net change and expire on phase exit.
        if (lead == _spokenLead || now < _nextLeadAt) return;
        _spokenLead = lead;
        _nextLeadAt = now + LeadCooldownSeconds;
        emit(lead == 1 ? ArenaAnnouncerCue.TakenTheLead :
            lead == -1 ? ArenaAnnouncerCue.LostTheLead : ArenaAnnouncerCue.TiedForTheLead);
    }

    private void UpdateLadder(ArenaAnnouncementSnapshot snapshot, Action<ArenaAnnouncerCue> emit)
    {
        int? cycle = snapshot.LadderCycle;
        if (!cycle.HasValue || cycle.Value < 0) return;
        if (!_highestCycle.HasValue) _highestCycle = cycle;
        else if (cycle.Value > _highestCycle.Value)
        {
            _highestCycle = cycle;
            emit(ArenaAnnouncerCue.LadderComplete);
        }

        // One Final Weapon per completed-ladder cycle, including a demotion and
        // re-promotion. A stale lower cycle cannot re-arm either announcement.
        if (snapshot.Live && !snapshot.Terminal && cycle == _highestCycle &&
            snapshot.WeaponCount > 0 && snapshot.WeaponTier == snapshot.WeaponCount - 1 &&
            _finalWeaponCycle < cycle.Value)
        {
            _finalWeaponCycle = cycle.Value;
            emit(ArenaAnnouncerCue.FinalWeapon);
        }
    }
}

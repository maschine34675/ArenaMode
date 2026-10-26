using System;
using System.Collections.Generic;

namespace ArenaMode.Modes;

/// <summary>
/// Objective-specific lifecycle. Physical eligibility belongs to the runtime;
/// this policy owns readiness, simulation-time accounting and a frozen result.
/// It deliberately does not reinterpret TDM kill totals as objective points.
/// </summary>
internal sealed class DominationMatchPolicy
{
    private readonly DominationPolicy _objectives = new DominationPolicy();
    private double _countdownEndsAt, _lastAdvancedAt;
    private float _lastStartUpdate = -1f;
    private bool _localUsec = true;

    internal DominationMatchPolicy(int scoreLimit = 200, int timeLimitSeconds = 600)
    {
        if (scoreLimit < 0 || scoreLimit > 1000) throw new ArgumentOutOfRangeException(nameof(scoreLimit));
        if (timeLimitSeconds < 0 || timeLimitSeconds > 7200) throw new ArgumentOutOfRangeException(nameof(timeLimitSeconds));
        ScoreLimit = scoreLimit;
        TimeLimitSeconds = timeLimitSeconds;
    }

    internal int ScoreLimit { get; }
    internal int TimeLimitSeconds { get; }
    internal TdmMatchPhase Phase { get; private set; }
    internal TdmMatchResult Result { get; private set; }
    internal string Reason { get; private set; }
    internal float StartedAt { get; private set; }
    internal float FinishedAt { get; private set; }
    internal IReadOnlyList<DominationPointState> Points => _objectives.Points;
    internal int UsecScore => _objectives.UsecScore;
    internal int BearScore => _objectives.BearScore;
    internal int FriendlyScore => _localUsec ? UsecScore : BearScore;
    internal int OpponentScore => _localUsec ? BearScore : UsecScore;

    internal void UpdateStart(float now, bool rosterReady)
    {
        if (!ValidTime(now) || now < _lastStartUpdate
            || Phase == TdmMatchPhase.Live || Phase == TdmMatchPhase.Finished) return;
        _lastStartUpdate = now;
        if (Phase == TdmMatchPhase.Assembling)
        {
            if (rosterReady)
            {
                Phase = TdmMatchPhase.Countdown;
                _countdownEndsAt = (double)now + TeamDeathmatchMatchPolicy.CountdownSeconds;
            }
            return;
        }
        if (!rosterReady) { Phase = TdmMatchPhase.Assembling; return; }
        if (now < _countdownEndsAt) return;
        // Slow release frames never charge the interval before combat starts.
        StartedAt = now;
        _lastAdvancedAt = now;
        Phase = TdmMatchPhase.Live;
    }

    internal void Advance(float now, IReadOnlyList<DominationOccupancy> occupancy, bool localUsec)
    {
        if (Phase != TdmMatchPhase.Live || !ValidTime(now) || now <= _lastAdvancedAt) return;
        double deadline = TimeLimitSeconds > 0 ? (double)StartedAt + TimeLimitSeconds : double.PositiveInfinity;
        double end = Math.Min(now, deadline);
        double duration = end - _lastAdvancedAt;
        if (duration <= 0d) return;
        if (duration > DominationPolicy.MaxStepSeconds)
        {
            // A long unsampled interval has no trustworthy occupancy history.
            // Discard it instead of inventing capture/income, then resume from
            // the new sample time. The real match deadline still applies.
            _lastAdvancedAt = end;
            _localUsec = localUsec;
            if (end >= deadline) Finish(end, false);
            return;
        }

        double applied;
        if (ScoreLimit > 0)
            applied = _objectives.AdvanceUntilScoreLimit(duration, occupancy, ScoreLimit);
        else
        {
            _objectives.Advance(duration, occupancy);
            applied = duration;
        }
        _localUsec = localUsec;
        _lastAdvancedAt += applied;
        bool scoreReached = ScoreLimit > 0 && Math.Max(UsecScore, BearScore) >= ScoreLimit;
        if (scoreReached || end >= deadline) Finish(_lastAdvancedAt, scoreReached);
    }

    private void Finish(double when, bool scoreReached)
    {
        Result = FriendlyScore > OpponentScore ? TdmMatchResult.Won
            : FriendlyScore < OpponentScore ? TdmMatchResult.Lost : TdmMatchResult.Draw;
        Reason = scoreReached ? "Score limit reached" : "Time limit reached";
        FinishedAt = (float)when;
        Phase = TdmMatchPhase.Finished;
    }

    internal float Elapsed(float now) => Phase == TdmMatchPhase.Finished
        ? Math.Max(0f, FinishedAt - StartedAt)
        : Phase == TdmMatchPhase.Live && ValidTime(now) ? Math.Max(0f, now - StartedAt) : 0f;

    internal int CountdownRemaining(float now) => Phase == TdmMatchPhase.Countdown && ValidTime(now)
        ? Math.Max(1, (int)Math.Ceiling(_countdownEndsAt - now)) : 0;

    private static bool ValidTime(float now) => !float.IsNaN(now) && !float.IsInfinity(now) && now >= 0f;
}

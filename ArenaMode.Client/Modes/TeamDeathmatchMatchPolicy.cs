using System;

namespace ArenaMode.Modes;

internal enum TdmMatchPhase { Assembling, Countdown, Live, Finished }
internal enum TdmMatchResult { None, Won, Lost, Draw }

/// <summary>
/// One raid's immutable rules and terminal decision. Times use the simulation
/// clock: a paused raid does not consume countdown or match time. This policy
/// owns no actors, configuration, UI, or native stop/respawn operations.
/// </summary>
internal sealed class TeamDeathmatchMatchPolicy
{
    internal const float CountdownSeconds = 3f;
    internal const float ResultSeconds = 15f;
    internal int KillLimit { get; }
    internal int TimeLimitSeconds { get; }
    internal TdmMatchPhase Phase { get; private set; }
    internal TdmMatchResult Result { get; private set; }
    internal string Reason { get; private set; }
    internal int FriendlyKills { get; private set; }
    internal int OpponentKills { get; private set; }
    internal float StartedAt { get; private set; }
    internal float FinishedAt { get; private set; }
    private float _countdownEndsAt;

    internal TeamDeathmatchMatchPolicy(int killLimit, int timeLimitSeconds)
    {
        if (killLimit < 0 || killLimit > 1000) throw new ArgumentOutOfRangeException(nameof(killLimit));
        if (timeLimitSeconds < 0 || timeLimitSeconds > 7200) throw new ArgumentOutOfRangeException(nameof(timeLimitSeconds));
        KillLimit = killLimit;
        TimeLimitSeconds = timeLimitSeconds;
    }

    internal void Update(float now, bool rosterReady, int friendlyKills, int opponentKills)
    {
        if (float.IsNaN(now) || float.IsInfinity(now) || Phase == TdmMatchPhase.Finished) return;
        if (Phase == TdmMatchPhase.Assembling)
        {
            if (rosterReady)
            {
                Phase = TdmMatchPhase.Countdown;
                _countdownEndsAt = now + CountdownSeconds;
            }
            return;
        }
        if (Phase == TdmMatchPhase.Countdown)
        {
            if (!rosterReady) { Phase = TdmMatchPhase.Assembling; return; }
            if (now < _countdownEndsAt) return;
            // A slow frame must not charge match time before combat was released.
            StartedAt = now;
            Phase = TdmMatchPhase.Live;
            return;
        }

        bool timeReached = TimeLimitSeconds > 0 && now - StartedAt >= TimeLimitSeconds;
        bool killsReached = KillLimit > 0 && Math.Max(friendlyKills, opponentKills) >= KillLimit;
        if (!timeReached && !killsReached) return;
        FriendlyKills = friendlyKills;
        OpponentKills = opponentKills;
        Result = friendlyKills > opponentKills ? TdmMatchResult.Won
            : friendlyKills < opponentKills ? TdmMatchResult.Lost : TdmMatchResult.Draw;
        Reason = killsReached ? "Kill limit reached" : "Time limit reached";
        FinishedAt = timeReached ? StartedAt + TimeLimitSeconds : now;
        Phase = TdmMatchPhase.Finished;
    }

    internal float Elapsed(float now) => Phase == TdmMatchPhase.Live
        ? Math.Max(0f, now - StartedAt)
        : Phase == TdmMatchPhase.Finished ? Math.Max(0f, FinishedAt - StartedAt) : 0f;

    internal int CountdownRemaining(float now) => Phase == TdmMatchPhase.Countdown
        ? Math.Max(1, (int)Math.Ceiling(_countdownEndsAt - now)) : 0;
}

using System;

namespace ArenaMode.Modes;

internal enum TacticalObjectiveSide : byte
{
    None,
    Friendly,
    Enemy,
}

/// <summary>
/// The capture point of a Tactical round (docs/reviews/TACTICAL-OBJECTIVE-20261108.md): pure
/// clock state, no bodies, no Unity. The point becomes active <see cref="AppearAfterSeconds"/>
/// after the round's combat start; a side standing on it alone accumulates hold progress and
/// completes the capture after <see cref="HoldSeconds"/>. Both sides present freeze the
/// progress, nobody present resets it, a change of holder restarts it, and a step longer than
/// <see cref="MaxStepSeconds"/> (a stall) awards nothing. The capture is reported exactly once.
/// </summary>
internal sealed class TacticalObjectivePolicy
{
    internal const float MinimumHoldSeconds = 3f;
    internal const float MaximumHoldSeconds = 60f;
    internal const float MaximumAppearSeconds = 300f;
    internal const float MaxStepSeconds = 30f;

    private float _startedAt = -1f;
    private float _lastAdvance = -1f;

    internal TacticalObjectivePolicy(float appearAfterSeconds, float holdSeconds)
    {
        AppearAfterSeconds = float.IsNaN(appearAfterSeconds) ? 0f
            : Math.Min(MaximumAppearSeconds, Math.Max(0f, appearAfterSeconds));
        HoldSeconds = float.IsNaN(holdSeconds) ? MinimumHoldSeconds
            : Math.Min(MaximumHoldSeconds, Math.Max(MinimumHoldSeconds, holdSeconds));
    }

    internal float AppearAfterSeconds { get; }
    internal float HoldSeconds { get; }

    /// <summary>A round's combat has begun and the clock runs.</summary>
    internal bool Started => _startedAt >= 0f;

    /// <summary>The point is up and can be held.</summary>
    internal bool Active { get; private set; }

    /// <summary>The side whose hold progress is accumulating; None while empty or contested from empty.</summary>
    internal TacticalObjectiveSide Holder { get; private set; }

    /// <summary>Hold progress of <see cref="Holder"/>, 0 to 1.</summary>
    internal float Progress { get; private set; }

    /// <summary>Both sides stood on the point at the last step.</summary>
    internal bool Contested { get; private set; }

    /// <summary>The side that completed the hold; terminal for the round.</summary>
    internal TacticalObjectiveSide Captured { get; private set; }

    internal void Begin(float now)
    {
        _startedAt = _lastAdvance = now;
        Active = false;
        Holder = TacticalObjectiveSide.None;
        Progress = 0f;
        Contested = false;
        Captured = TacticalObjectiveSide.None;
    }

    internal void Reset()
    {
        _startedAt = _lastAdvance = -1f;
        Active = false;
        Holder = TacticalObjectiveSide.None;
        Progress = 0f;
        Contested = false;
        Captured = TacticalObjectiveSide.None;
    }

    /// <summary>Seconds until the point appears; zero when it is up, not started or already captured.</summary>
    internal float UntilActive(float now)
        => !Started || Active || Captured != TacticalObjectiveSide.None
            ? 0f : Math.Max(0f, _startedAt + AppearAfterSeconds - now);

    /// <summary>
    /// One step with the current occupancy. Returns the side that completed the hold in this
    /// step, otherwise None. Time that runs backwards or is not a number changes nothing.
    /// </summary>
    internal TacticalObjectiveSide Advance(float now, int friendlyOnPoint, int enemyOnPoint)
    {
        if (!Started || Captured != TacticalObjectiveSide.None) return TacticalObjectiveSide.None;
        if (float.IsNaN(now) || float.IsInfinity(now) || now < _lastAdvance) return TacticalObjectiveSide.None;
        float dt = now - _lastAdvance;
        _lastAdvance = now;
        if (dt > MaxStepSeconds) dt = 0f;
        if (!Active)
        {
            if (now - _startedAt >= AppearAfterSeconds) Active = true;
            else return TacticalObjectiveSide.None;
        }
        Contested = friendlyOnPoint > 0 && enemyOnPoint > 0;
        if (Contested) return TacticalObjectiveSide.None;
        var present = friendlyOnPoint > 0 ? TacticalObjectiveSide.Friendly
            : enemyOnPoint > 0 ? TacticalObjectiveSide.Enemy : TacticalObjectiveSide.None;
        if (present == TacticalObjectiveSide.None)
        {
            Holder = TacticalObjectiveSide.None;
            Progress = 0f;
            return TacticalObjectiveSide.None;
        }
        if (Holder != present) { Holder = present; Progress = 0f; }
        Progress = Math.Min(1f, Progress + dt / HoldSeconds);
        if (Progress < 1f - 1e-5f) return TacticalObjectiveSide.None;
        Progress = 1f;
        Captured = Holder;
        return Captured;
    }

    /// <summary>The HUD tail for the status line; empty before the round's combat start.</summary>
    internal string Describe(float now)
    {
        if (!Started) return string.Empty;
        if (Captured != TacticalObjectiveSide.None)
            return Captured == TacticalObjectiveSide.Friendly ? "POINT held by you" : "POINT held by enemy";
        if (!Active) return "POINT in " + (int)Math.Ceiling(UntilActive(now)) + "s";
        if (Contested) return "POINT contested";
        if (Holder == TacticalObjectiveSide.None) return "POINT free";
        return (Holder == TacticalObjectiveSide.Friendly ? "POINT you " : "POINT enemy ") + (int)(Progress * 100f) + "%";
    }
}

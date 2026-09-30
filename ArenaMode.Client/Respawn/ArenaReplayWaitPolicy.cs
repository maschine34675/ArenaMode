using ArenaMode.Spectator;
using System;

namespace ArenaMode.Respawn;

internal enum ArenaReplayWaitDecision { Ready, Wait, Unsafe }

/// <summary>Per-death bound, in simulation seconds like KillcamReplay's lead-in.</summary>
internal sealed class ArenaReplayWaitPolicy
{
    private float? _deadline;

    internal ArenaReplayWaitDecision Observe(OptionalCameraOwnerState owner, float remaining, float now)
    {
        if (owner == OptionalCameraOwnerState.Absent || owner == OptionalCameraOwnerState.Idle)
            return ArenaReplayWaitDecision.Ready;
        if (owner != OptionalCameraOwnerState.Busy || !Finite(now) || !Finite(remaining) || remaining < 0f)
            return ArenaReplayWaitDecision.Unsafe;

        // Never slide the deadline forward on each frame. StopPlayback cannot
        // cancel the mod's pending lead-in, so a timeout must NOT release a body.
        _deadline ??= now + Math.Max(5f, Math.Min(75f, remaining + 5f));
        return now < _deadline.Value ? ArenaReplayWaitDecision.Wait : ArenaReplayWaitDecision.Unsafe;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
